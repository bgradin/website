import React, { Component } from "react";

class App extends Component {
  render() {
    const { title, content } = this.props;

    return (
      <main>
        <h1>{title}</h1>
        <p>{content}</p>
      </main>
    );
  }
}

export default App;
