import React from "react";

function Meta(props) {
  const { charset } = props;

  return <meta charset={charset || "utf-8"} />;
}

export default Meta;
