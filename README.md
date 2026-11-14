# gradinware.com

[![CI](https://github.com/bgradin/website/actions/workflows/ci.yml/badge.svg)](https://github.com/bgradin/website/actions/workflows/ci.yml)

Public portfolio + personal website for the Gradins

## Dependencies

- [Docker](https://www.docker.com/)
- [Yarn](https://yarnpkg.com/)
- .NET 10 SDK for direct local builds (version selected by [`global.json`](global.json))

## Developing

I'm developing on Windows using [WSL](https://docs.microsoft.com/en-us/windows/wsl/about) through [VSCode](https://code.visualstudio.com/). Theoretically this code should compile on anywhere on 64-bit linux.

Run `make` to start a dev server at [localhost:5000](http://localhost:5000). `make help` lists all available options.

The application requires a JWT signing key at runtime. Generate a private local key and export it before starting the container:

```sh
export PRODUCTION_JWT_KEY="$(openssl rand -hex 32)"
make
```

For deployment, set `PRODUCTION_JWT_KEY` from the deployment secret store. Add the same secret to Travis CI as a protected environment variable for tagged builds. The container maps it to `OAuth__JwtKey` at startup; the value is not embedded in the image. The runtime host must pass `PRODUCTION_JWT_KEY` to the container as well. Content authoring access also requires `Content__AdminUserId` to be set to the numeric account ID allowed to administer content. Password reset requires `Email__SmtpHost`, `Email__From`, and `PasswordReset__Url`; configure `Email__SmtpPort`, `Email__Username`, `Email__Password`, and `Email__UseSsl` as needed for the mail provider.

## Deployment

GitHub Actions builds the frontend and Docker image on pushes and pull requests.
Pushing a tag also publishes the Docker image to `www.gradinware.com/gradinware/server`
with both the tag name and `latest`.

Configure the `REGISTRY_USERNAME` and `REGISTRY_PASSWORD` repository secrets in
GitHub under **Settings → Secrets and variables → Actions** to enable publishing.

## TODO

- Improve account API
- Improve content import/create endpoint
- Create site editor
- Create site
