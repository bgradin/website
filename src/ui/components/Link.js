import React from "react";

function Link(props) {
  const { rel, href } = props;

  return <link rel={rel || "stylesheet"} href={href} />;
}

export default Link;
