import React from "react";

function Page(props) {
  const { head, body } = props;

  return (
    <html lang="en">
      <head>{head}</head>
      <body>{body}</body>
    </html>
  );
}

export default Page;
