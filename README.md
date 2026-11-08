# gradinware.com

[![CI](https://github.com/bgradin/website/actions/workflows/ci.yml/badge.svg)](https://github.com/bgradin/website/actions/workflows/ci.yml)

Public portfolio + personal website for the Gradins

## Dependencies

- [Docker](https://www.docker.com/)
- [Yarn](https://yarnpkg.com/)

## Developing

I'm developing on Windows using [WSL](https://docs.microsoft.com/en-us/windows/wsl/about) through [VSCode](https://code.visualstudio.com/). Theoretically this code should compile on anywhere on 64-bit linux.

Run `make` to start a dev server at [localhost:5000](http://localhost:5000). `make help` lists all available options.

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
