export default function template(patch, content) {
  const closingTag = content.indexOf("</head>");
  if (closingTag === -1) {
    return content;
  }

  return `${content.slice(0, closingTag)}<script>
    window.__patch = ${JSON.stringify(patch)}
  </script>${content.slice(closingTag)}`;
}
