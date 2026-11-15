import React from "react";

function Script(props) {
  const { src, type, code, defer } = props;

  if (src) {
    return <script src={src} defer={defer}></script>;
  }

  return <script type={type}>{code}</script>;
}

export default Script;
