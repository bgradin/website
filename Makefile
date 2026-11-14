IMAGE = gradinware-server
CONTAINER = gradinware-server-dev

.DEFAULT_GOAL := start

.PHONY: stop start destroy rebuild create help frontend node_modules

stop: ## Stop dev server
	docker stop $(CONTAINER) >/dev/null 2>&1 || true

start: create ## Start dev server
	@if docker inspect $(CONTAINER) >/dev/null 2>&1; then docker start $(CONTAINER); else docker run -dit -p 5000:5000 -v $(shell pwd)/data:/data -e PRODUCTION_JWT_KEY -e Content__AdminUserId --name $(CONTAINER) $(IMAGE); fi

destroy: stop ## Destroy dev environment
	docker rm $(CONTAINER) >/dev/null 2>&1 || true
	docker rmi $(IMAGE) >/dev/null 2>&1 || true

rebuild: destroy start ## Rebuild dev environment

create: frontend ## Build dev image
	docker build -t $(IMAGE) .

frontend: node_modules ## Build frontend artifacts
	yarn build:frontend

help: ## List available make commands
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "\033[36m%-30s\033[0m %s\n", $$1, $$2}'

node_modules: package.json yarn.lock
	yarn install --frozen-lockfile --non-interactive
	touch $@
