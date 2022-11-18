export default function template(title, state, content) {
  return `<!DOCTYPE html>
  <html lang="en">
  <head>
    <meta charset="utf-8">
    <title>${title}</title>
    <link rel="stylesheet" href="https://cdn.simplecss.org/simple.css">
    <link rel="stylesheet" href="/assets/main.css">
  </head>
  <body>
    <div class="content">
      <div id="app">${content}</div>
    </div>
  
    <script>
      window.__STATE__ = ${JSON.stringify(state)}
    </script>
    <script src="/assets/main.js"></script>
  </body>`;
}
