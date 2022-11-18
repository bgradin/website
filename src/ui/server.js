import express from 'express'
import React from 'react'
import { renderToString } from 'react-dom/server'
import App from './components/app'
import template from './template'

function render(state) {
  let content = renderToString(
    <App {...state} />
  );

  return {content, state};
}

const app = express()
app.disable('x-powered-by');
app.listen(3000);

app.use(express.json());

app.get('/', (req, res) => {
  const { state, content}  = render(req.body)
  const response = template(req.url, state, content)
  res.send(response);
});
