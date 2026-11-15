IMAGE = gradinware-server
CONTAINER = gradinware-server-dev
ENV_FILE ?= .env

# Docker reads dotenv values as data; never evaluate secrets as Make or shell code.
LOCAL_ENV_FILE = $(if $(wildcard $(ENV_FILE)),--env-file "$(ENV_FILE)")
RUNTIME_VARIABLES = PRODUCTION_JWT_KEY \
                    PRODUCTION_RECAPTCHA_SITE_KEY \
                    PRODUCTION_RECAPTCHA_SECRET_KEY \
                    PRODUCTION_RECAPTCHA_HOSTNAME \
                    OAuth__JwtKey Recaptcha__SiteKey Recaptcha__SecretKey \
                    Recaptcha__Hostname Content__AdminUserId
RUNTIME_OVERRIDES = $(foreach variable,$(RUNTIME_VARIABLES),$(if $(filter undefined,$(origin $(variable))),,-e $(variable)))

.DEFAULT_GOAL := start

.PHONY: stop start destroy rebuild create help frontend node_modules

stop: ## Stop dev server
	docker stop $(CONTAINER) >/dev/null 2>&1 || true

start: create ## Start dev server
	@if docker inspect $(CONTAINER) >/dev/null 2>&1; then \
		docker start $(CONTAINER); \
	else \
		docker run -dit -p 5000:5000 -v $(shell pwd)/data:/data \
			-v "$(shell pwd)/src/core/pages:/app/pages:ro" \
			$(LOCAL_ENV_FILE) $(RUNTIME_OVERRIDES) \
			-e ASPNETCORE_ENVIRONMENT=Development \
			--name $(CONTAINER) $(IMAGE); \
	fi

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
