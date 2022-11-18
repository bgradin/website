const fs = require("fs");
const sass = require("node-sass");

const OUTFILE = "src/ui/assets/main.css";

sass.render({
  file: "src/ui/styles/main.scss",
}, (err, result) => {
  if (err) {
    console.error(err);
    return;
  }

  fs.writeFile("src/ui/assets/main.css", result.css, (err) => {
    if (err){
      console.error(err);
    }
  });
});
