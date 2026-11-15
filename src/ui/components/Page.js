import React from "react";

function Page(props) {
  const { head, body } = props;

  return (
    <html lang="en">
      <head>{head}</head>
      <body>
        {body}
        <script src="/assets/account.js"></script>
      </body>
    </html>
  );
}

export default Page;
