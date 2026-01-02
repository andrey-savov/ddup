# Contributing to ddup

First off, thank you for considering contributing to ddup! It's people like you that make ddup such a great tool.

## Code of Conduct

This project and everyone participating in it is governed by our Code of Conduct. By participating, you are expected to uphold this code. Please report unacceptable behavior to the project maintainers.

## How Can I Contribute?

### Reporting Bugs

Before creating bug reports, please check the issue list as you might find out that you don't need to create one. When you are creating a bug report, please include as many details as possible:

* **Use a clear and descriptive title**
* **Describe the exact steps which reproduce the problem**
* **Provide specific examples to demonstrate the steps**
* **Describe the behavior you observed after following the steps**
* **Explain which behavior you expected to see instead and why**
* **Include screenshots and animated GIFs if possible**
* **Include your environment details** (OS, .NET version, ddup version)

### Suggesting Enhancements

Enhancement suggestions are tracked as GitHub issues. When creating an enhancement suggestion, please include:

* **Use a clear and descriptive title**
* **Provide a step-by-step description of the suggested enhancement**
* **Provide specific examples to demonstrate the steps**
* **Describe the current behavior and the expected behavior**
* **Explain why this enhancement would be useful**

### Pull Requests

* Follow the [C# coding conventions](https://learn.microsoft.com/en-us/dotnet/fundamentals/coding-style/coding-conventions)
* Write meaningful commit messages
* Include appropriate test cases
* Update the README.md and any other relevant documentation
* End all files with a newline
* Avoid platform-specific code where possible (consider cross-platform implications)

## Development Setup

1. **Clone the repository**
   ```bash
   git clone <repository-url>
   cd ddup
   ```

2. **Install dependencies**
   - [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

3. **Build the project**
   ```bash
   dotnet build
   ```

4. **Run tests**
   ```bash
   dotnet test
   ```

5. **Run the application**
   ```bash
   dotnet run -- --help
   ```

## Styleguides

### C# Code Style

- Use `camelCase` for local variables and parameters
- Use `PascalCase` for classes, properties, and methods
- Use `UPPER_CASE` for constants
- Use meaningful variable names
- Add XML documentation comments for public types and members
- Keep lines under 120 characters when possible
- Use `var` for obvious types, explicit types for clarity

### Commit Messages

* Use the present tense ("Add feature" not "Added feature")
* Use the imperative mood ("Move cursor to..." not "Moves cursor to...")
* Limit the first line to 72 characters or less
* Reference issues and pull requests liberally after the first line
* Consider starting the commit message with an applicable emoji:
  * 🎨 `:art:` - Improve structure/format
  * 🐛 `:bug:` - Fix bug
  * ✨ `:sparkles:` - Introduce new feature
  * 📚 `:books:` - Documentation
  * 🔧 `:wrench:` - Update configuration
  * ⚡ `:zap:` - Improve performance
  * ✅ `:white_check_mark:` - Add tests

## Additional Notes

### Issue and Pull Request Labels

This section lists the labels we use to help organize and categorize issues and pull requests.

* `bug` - Something isn't working
* `enhancement` - New feature or request
* `documentation` - Improvements or additions to documentation
* `good first issue` - Good for newcomers
* `help wanted` - Extra attention is needed
* `question` - Further information is requested
* `wontfix` - This will not be worked on

## Recognition

Contributors will be recognized in:
- The README.md file
- Release notes
- Project documentation

## License

By contributing to ddup, you agree that your contributions will be licensed under its MIT License.

## Questions?

Don't hesitate to ask questions by creating an issue with the `question` label.

Thank you for contributing! 🎉
