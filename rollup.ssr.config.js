import svelte from 'rollup-plugin-svelte';
import commonjs from '@rollup/plugin-commonjs';
import resolve from '@rollup/plugin-node-resolve';
import sveltePreprocess from 'svelte-preprocess';
import css from 'rollup-plugin-css-only';

export default {
	input: 'src/ui/App.svelte',
	output: {
		sourcemap: true,
		format: 'iife',
		name: 'app',
		file: 'src/core/wwwroot/ssr.js'
	},
	plugins: [
		svelte({
			preprocess: sveltePreprocess(),
			compilerOptions: {
				dev: true,
				generate: "ssr",
			}
		}),
        css({ output: 'src/core/wwwroot/ssr.css' }),

		resolve({
			browser: true,
			dedupe: ['svelte']
		}),
		commonjs(),
	],
};
