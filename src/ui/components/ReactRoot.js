import React from "react";

function ReactRoot(props) {
  const { children } = props;

  return (
    <div class="content">
      <div id="app">{children}</div>
    </div>
  );
}

export default ReactRoot;
