import React from "react";

function ReactRoot(props) {
  const { children } = props;

  return (
    <div className="content">
      <div id="app">{children}</div>
    </div>
  );
}

export default ReactRoot;
