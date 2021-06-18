import App from './App.svelte';

declare global {
	interface Window {
		__INITIAL_DATA__: any;
	}
}

const app = new App({
	target: document.body,
	hydrate: true,
	props: window.__INITIAL_DATA__,
});

export default app;
