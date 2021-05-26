IMAGE = gradinware/server
CONTAINER = gradinware/server-dev

.DEFAULT_GOAL := start

.PHONY: stop start destroy create dev help
.SILENT: stop start destroy create node_modules

stop: ## Stop dev server
	docker stop $(CONTAINER) >/dev/null 2>&1 || true

start: create ## Start dev server
	docker inspect $(CONTAINER) >/dev/null 2>&1 || docker run -dit -p 5000:5000 -v $(shell pwd)/src/ui/public:/public --name $(CONTAINER) $(IMAGE)
	docker start $(CONTAINER) >/dev/null 2>&1 || true

destroy: stop ## Destroy dev environment
	docker rm $(CONTAINER) >/dev/null 2>&1 || true
	docker rmi $(IMAGE) >/dev/null 2>&1 || true

create: node_modules ## Build dev image
	docker inspect $(IMAGE) >/dev/null 2>&1 || docker build -t $(IMAGE) .

dev: start ## Start live reload server
	yarn dev

help: ## List available make commands
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "\033[36m%-30s\033[0m %s\n", $$1, $$2}'

node_modules: package.json yarn.lock
	yarn
	touch $@
