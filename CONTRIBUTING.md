# Contributing
Welcome and thank you for your interest in contributing to the Microsoft.Azure.StackExchangeRedis extension! Before contributing to this project, please review this document for policies and procedures which
will ease the contribution and review process for everyone. 

## Issues and Feature Requests
For any issues or feature requests, please create a new issue and attach the most relevant label. Also, please include the following information:
- The package version you are using
- Exception messages
- Stack traces
- Instructions for reproducing the problem

## Style Guidelines
Please refer to the following documents for coding styles:
- [C# Coding Conventions](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- [General Naming Conventions](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/general-naming-conventions)

## Running Tests
The `tests` directory contains a unit test project. To run the tests use Visual Studio or:
```
dotnet test <path to Microsoft.Azure.StackExchangeRedis.Tests.csproj>
```

## Pull Request Process
1. Add or update unit tests to ensure all code changes are covered
1. Ensure that all tests are passing
1. Add your change to [`RELEASENOTES.md`]
1. Update any documentation impacted by your changes
1. Work with repo maintainers to get your PR approved and merged

## Release Process
1. Increment the version number in [Microsoft.Azure.StackExchangeRedis.csproj](src\Microsoft.Azure.StackExchangeRedis.csproj). The versioning scheme we use is [SemVer](https://semver.org/).
1. Work with a repo maintainer to have an official build created and a new package pushed to nuget.org

## License Information
This project uses the MIT License. See more [LICENSE](LICENSE)