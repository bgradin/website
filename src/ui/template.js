export default function template(patch, content) {
  const closingTag = content.indexOf("</head>");
  if (closingTag === -1) {
    return content;
  }

  const safePatch = JSON.stringify(patch)
    .replace(/</g, "\\u003c")
    .replace(/>/g, "\\u003e")
    .replace(/&/g, "\\u0026")
    .replace(/\u2028/g, "\\u2028")
    .replace(/\u2029/g, "\\u2029");

  return `${content.slice(0, closingTag)}<script>
    window.__patch = ${safePatch}
  </script>${content.slice(closingTag)}`;
}
