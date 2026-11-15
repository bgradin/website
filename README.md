# gradinware.com

[![CI](https://github.com/bgradin/website/actions/workflows/ci.yml/badge.svg)](https://github.com/bgradin/website/actions/workflows/ci.yml)

Public portfolio and personal website for the Gradins.

## Dependencies

- [Docker](https://www.docker.com/) for container builds and local hosting.
- [GNU Make](https://www.gnu.org/software/make/) for development commands.
- [Node.js 24](https://nodejs.org/) and [Yarn 1.22.22](https://yarnpkg.com/) for
  frontend builds.
- [.NET 10 SDK](https://dotnet.microsoft.com/) for direct backend builds and
  tests; SDK selection is defined in [global.json](global.json).
- Python 3 for runtime configuration tests.

Development on Windows uses [WSL](https://docs.microsoft.com/en-us/windows/wsl/)
and [VS Code](https://code.visualstudio.com/).

## Building

Build the frontend and Docker image:

```sh
make create
```

To build the frontend or backend independently:

```sh
yarn install --frozen-lockfile --non-interactive
yarn build:frontend
dotnet build src/core/Gradinware.csproj
```

The Dockerfile builds the frontend and backend in separate stages and packages
them into the runtime image.

## Developing

Run `make` to build and start the development container at
[localhost:5000](http://localhost:5000). Run `make help` to list available commands.

| Command         | Purpose                                                   |
| --------------- | --------------------------------------------------------- |
| `make frontend` | Rebuild frontend assets.                                  |
| `make stop`     | Stop the development container.                           |
| `make rebuild`  | Rebuild the image and recreate the development container. |
| `make destroy`  | Remove the development container and image.               |

Backend code and bundled pages live in `src/core`. React code, Sass, and public
assets live in `src/ui`. Local SQLite data persists in the `data` directory.
See [Quilt documentation](docs/QUILT.md) for the content model.

Run the backend and runtime configuration checks:

```sh
dotnet test tests/Quilt.Authorization.Tests/Quilt.Authorization.Tests.csproj
dotnet run --project tests/ContentStorage/ContentStorage.csproj
python3 tests/Configuration/configuration.py
```

## Deploying

[CI](.github/workflows/ci.yml) builds the frontend, runs checks, and builds the
Docker image on pushes and pull requests.

To release to production:

1. Open **Actions → Reviewed production release** in GitHub.
2. Select **Run workflow** and choose the `master` branch.
3. Complete any required approvals for the `registry-publish` and `production`
   environments.
4. Confirm that the publish and deploy jobs succeed.

The [release workflow](.github/workflows/release.yml) tests and builds the site,
publishes an immutable image reference to `www.gradinware.com/gradinware/server`,
and deploys that image by digest over SSH. It then checks that the production
homepage returns HTTP 200.
