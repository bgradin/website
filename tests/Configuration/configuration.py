"""Check runtime configuration without Docker, application services, or real secrets."""

import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SETTINGS = {
    "PRODUCTION_JWT_KEY": "OAuth__JwtKey",
    "PRODUCTION_RECAPTCHA_SITE_KEY": "Recaptcha__SiteKey",
    "PRODUCTION_RECAPTCHA_SECRET_KEY": "Recaptcha__SecretKey",
    "PRODUCTION_RECAPTCHA_HOSTNAME": "Recaptcha__Hostname",
}
RUNTIME_VARIABLES = [*SETTINGS, *SETTINGS.values(), "Content__AdminUserId"]


class ConfigurationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        self.bin = self.directory / "bin"
        self.bin.mkdir()
        self.environment = os.environ.copy()
        for name in RUNTIME_VARIABLES:
            self.environment.pop(name, None)
        self.environment["PATH"] = str(self.bin) + os.pathsep + self.environment["PATH"]
        self.environment["CONFIGURATION_CAPTURE"] = str(self.directory / "capture.json")

    def executable(self, name, code):
        path = self.bin / name
        path.write_text(code)
        path.chmod(0o700)

    def run_entrypoint(self):
        self.executable("node", '#!/bin/sh\nif [ "$1" = "-e" ]; then exit 0; fi\nexec sleep 60\n')
        self.executable("dotnet", """#!/usr/bin/env python3
import json, os
from pathlib import Path
names = ['OAuth__JwtKey', 'Recaptcha__SiteKey', 'Recaptcha__SecretKey', 'Recaptcha__Hostname']
Path(os.environ['CONFIGURATION_CAPTURE']).write_text(json.dumps({name: os.environ.get(name) for name in names}))
""")
        subprocess.run(
            ["bash", str(ROOT / "entrypoint.sh")], cwd=self.directory,
            env=self.environment, check=True, capture_output=True, text=True,
        )
        return json.loads(Path(self.environment["CONFIGURATION_CAPTURE"]).read_text())

    def test_production_settings_override_application_settings(self):
        for production_name, application_name in SETTINGS.items():
            self.environment[production_name] = "production-test-value"
            self.environment[application_name] = "local-test-value"
        captured = self.run_entrypoint()
        self.assertTrue(all(value == "production-test-value" for value in captured.values()))

    def test_empty_production_settings_preserve_application_settings(self):
        for production_name, application_name in SETTINGS.items():
            self.environment[production_name] = ""
            self.environment[application_name] = "local-test-value"
        captured = self.run_entrypoint()
        self.assertTrue(all(value == "local-test-value" for value in captured.values()))

    def test_absent_production_settings_preserve_application_settings(self):
        for application_name in SETTINGS.values():
            self.environment[application_name] = "local-test-value"
        captured = self.run_entrypoint()
        self.assertTrue(all(value == "local-test-value" for value in captured.values()))

    def check_supervision(self, renderer_exit=None, backend_exit=None):
        # Each fake service records its PID. The renderer waits for backend
        # startup so this exercises supervision after the readiness check.
        self.executable("node", """#!/usr/bin/env python3
import os, sys, time
from pathlib import Path
if sys.argv[1] == '-e':
    sys.exit(0)
Path('node.pid').write_text(str(os.getpid()))
while not Path('dotnet.pid').exists():
    time.sleep(0.01)
if os.environ.get('TEST_RENDERER_EXIT') is not None:
    sys.exit(int(os.environ['TEST_RENDERER_EXIT']))
while True:
    time.sleep(1)
""")
        self.executable("dotnet", """#!/usr/bin/env python3
import os, sys, time
from pathlib import Path
Path('dotnet.pid').write_text(str(os.getpid()))
if os.environ.get('TEST_BACKEND_EXIT') is not None:
    sys.exit(int(os.environ['TEST_BACKEND_EXIT']))
while True:
    time.sleep(1)
""")
        if renderer_exit is not None:
            self.environment['TEST_RENDERER_EXIT'] = str(renderer_exit)
        if backend_exit is not None:
            self.environment['TEST_BACKEND_EXIT'] = str(backend_exit)
        result = subprocess.run(
            ["bash", str(ROOT / "entrypoint.sh")], cwd=self.directory,
            env=self.environment, capture_output=True, text=True, timeout=5,
        )
        expected = (renderer_exit or 1) if renderer_exit is not None else backend_exit
        self.assertEqual(result.returncode, expected, result.stderr)
        for service in ['node', 'dotnet']:
            pid = int((self.directory / (service + '.pid')).read_text())
            with self.assertRaises(ProcessLookupError):
                os.kill(pid, 0)

    def test_renderer_failure_stops_backend(self):
        self.check_supervision(renderer_exit=7)

    def test_unexpected_clean_renderer_exit_stops_backend(self):
        self.check_supervision(renderer_exit=0)

    def test_backend_failure_stops_renderer(self):
        self.check_supervision(backend_exit=9)

    def run_make(self, *arguments):
        shutil.copy(ROOT / "Makefile", self.directory / "Makefile")
        (self.directory / "package.json").write_text("{}")
        (self.directory / "yarn.lock").touch()
        self.executable("yarn", "#!/bin/sh\nexit 0\n")
        self.executable("docker", """#!/usr/bin/env python3
import json, os, sys
from pathlib import Path
if sys.argv[1] == 'inspect':
    sys.exit(1)
if sys.argv[1] == 'run':
    Path(os.environ['CONFIGURATION_CAPTURE']).write_text(json.dumps(sys.argv[2:]))
""")
        subprocess.run(
            ["make", "start", *arguments], cwd=self.directory,
            env=self.environment, check=True, capture_output=True, text=True,
        )
        return json.loads(Path(self.environment["CONFIGURATION_CAPTURE"]).read_text())

    def test_local_dotenv_is_passed_as_data_with_environment_overrides(self):
        marker = self.directory / "must-not-execute"
        (self.directory / ".env").write_text(f"OAuth__JwtKey=$(touch {marker})\n")
        for name in RUNTIME_VARIABLES:
            self.environment[name] = f"$(shell touch {marker})"
        arguments = self.run_make()
        self.assertEqual(arguments[arguments.index("--env-file") + 1], ".env")
        overrides = [arguments[index + 1] for index, value in enumerate(arguments) if value == "-e"]
        self.assertEqual(overrides, RUNTIME_VARIABLES + ["ASPNETCORE_ENVIRONMENT=Development"])
        self.assertFalse(marker.exists())
        self.assertNotIn(f"$(shell touch {marker})", arguments)

    def test_local_dotenv_is_optional(self):
        arguments = self.run_make()
        self.assertNotIn("--env-file", arguments)
        overrides = [arguments[index + 1] for index, value in enumerate(arguments) if value == "-e"]
        self.assertEqual(overrides, ["ASPNETCORE_ENVIRONMENT=Development"])
        self.assertIn(str(self.directory / "src/core/pages") + ":/app/pages:ro", arguments)

    def test_explicit_empty_environment_value_overrides_dotenv(self):
        (self.directory / ".env").write_text("Recaptcha__SiteKey=file-test-value\n")
        self.environment["Recaptcha__SiteKey"] = ""
        arguments = self.run_make()
        self.assertEqual(arguments[arguments.index("-e") + 1], "Recaptcha__SiteKey")

    def test_custom_dotenv_path(self):
        dotenv = self.directory / "local.env"
        dotenv.write_text("OAuth__JwtKey=test-value\n")
        arguments = self.run_make("ENV_FILE=" + str(dotenv))
        self.assertEqual(arguments[arguments.index("--env-file") + 1], str(dotenv))


if __name__ == "__main__":
    unittest.main()
