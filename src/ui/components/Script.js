import React from "react";

function Script(props) {
  const { src, type, code } = props;

  if (src) {
    return <script src={src}></script>;
  }

  return <script type={type}>{code}</script>;
}

export default Script;
