IMAGE = gradinware-server
CONTAINER = gradinware-server-dev

.DEFAULT_GOAL := start

.PHONY: stop start destroy rebuild create help frontend node_modules

stop: ## Stop dev server
	docker stop $(CONTAINER) >/dev/null 2>&1 || true

start: create ## Start dev server
	docker inspect $(CONTAINER) >/dev/null 2>&1 || docker run -dit -p 5000:5000 -p 3000:3000 -v $(shell pwd)/data:/data --name $(CONTAINER) $(IMAGE)
	docker start $(CONTAINER) >/dev/null 2>&1 || true

destroy: stop ## Destroy dev environment
	docker rm $(CONTAINER) >/dev/null 2>&1 || true
	docker rmi $(IMAGE) >/dev/null 2>&1 || true

rebuild: destroy start ## Rebuild dev environment

create: frontend ## Build dev image
	docker inspect $(IMAGE) >/dev/null 2>&1 || docker build -t $(IMAGE) .

frontend: node_modules ## Build frontend artifacts
	./node_modules/esbuild-linux-64/bin/esbuild src/ui/client.js \
		--bundle \
		--minify \
		--sourcemap \
		--loader:.js=jsx \
		--jsx=transform \
		--target=chrome58,firefox57,safari11,edge18 \
		--outfile=src/ui/public/assets/main.js
	npx node-sass src/ui/styles/main.scss --source-map=src/ui/public/assets/main.css.map src/ui/public/assets/main.css

help: ## List available make commands
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "\033[36m%-30s\033[0m %s\n", $$1, $$2}'

node_modules: package.json yarn.lock
	yarn
	touch $@
