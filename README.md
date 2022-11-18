# gradinware.com

[![Build Status](https://travis-ci.com/bgradin/website.svg?branch=master)](https://travis-ci.com/bgradin/website)

Public portfolio + personal website for the Gradins

## Dependencies

- [Docker](https://www.docker.com/)
- [Yarn](https://yarnpkg.com/)

## Developing

I'm developing on Windows using [WSL](https://docs.microsoft.com/en-us/windows/wsl/about) through [VSCode](https://code.visualstudio.com/). Theoretically this code should compile on anywhere on 64-bit linux.

Run `make` to start a dev server at [localhost:5000](http://localhost:5000). `make help` lists all available options.

## Deployment

All builds trigger Travis CI, but tags will additionally deploy the site code to my VPS.
