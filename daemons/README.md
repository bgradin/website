I've added yaml files here for the daemons which serve gradinware.com. Some files/directories are intentionally left untracked for security reasons:
- `auth/registry.password` (docker registry credentials) - you can create this with `htpasswd`
- `data` - (docker registry datastore) - this should be created before running `docker-compose` on `registry.yml`
- `watchtower.json` (watchtower configuration) - this also contains credentials for docker registry. See the [watchtower docs](https://containrrr.dev/watchtower/private-registries/) for how to create this file.