# Pull Request Review Instructions

## Overview

This document provides comprehensive guidelines for reviewing pull requests in the Azure Functions .NET Template project. This project uses Azure Functions with .NET 8 isolated worker model, GitHub Actions, and Infrastructure as Code (Bicep) to provide automated, secure, and scalable function deployment.

## 🔍 Review Checklist

### 1. Code Quality & Standards

#### C# Code Review
- [ ] **Nullable Reference Types**: Ensure proper nullable annotations and null checks
- [ ] **Error Handling**: Verify proper try-catch blocks with meaningful error messages
- [ ] **Logging**: Check that appropriate `ILogger` usage exists for troubleshooting
- [ ] **Async/Await**: Ensure proper async patterns, no blocking calls
- [ ] **Input Validation**: Verify function parameters and request inputs are validated
- [ ] **Code Comments**: Review XML documentation comments for public members

```csharp
// Good Example
[Function("MyFunction")]
public async Task<IActionResult> Run(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
{
    _logger.LogInformation("Processing request for {Url}", req.Path);
    
    try
    {
        var name = req.Query["name"].FirstOrDefault();
        if (string.IsNullOrEmpty(name))
        {
            return new BadRequestObjectResult(new { error = "Name parameter is required" });
        }
        return new OkObjectResult(new { message = $"Hello, {name}!" });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error processing request");
        return new StatusCodeResult(500);
    }
}
```

#### General Code Quality
- [ ] **No hardcoded secrets**: Verify no passwords, API keys, or sensitive data in code
- [ ] **Configuration externalized**: Check that environment-specific values use configuration
- [ ] **Consistent naming**: Verify consistent naming conventions throughout
- [ ] **Code reusability**: Look for opportunities to reduce duplication

### 2. Security Review

#### Authentication & Authorization
- [ ] **Managed Identity Usage**: Verify proper use of Azure Managed Identity
- [ ] **Token Handling**: Check that tokens are not logged or exposed
- [ ] **API Permissions**: Verify least-privilege access to external APIs
- [ ] **OIDC Configuration**: Ensure proper OIDC federated credential usage in workflows

#### Secrets Management
- [ ] **No secrets in code**: Verify no hardcoded passwords or connection strings
- [ ] **GitHub Secrets**: Check that sensitive values use GitHub repository secrets
- [ ] **Environment Variables**: Verify sensitive data accessed via configuration only

```csharp
// ✅ Good - Using configuration
var connectionString = Configuration["DatabaseConnectionString"];

// ❌ Bad - Hardcoded sensitive data
var connectionString = "Server=myserver;Database=mydb;Password=secret123";
```

### 3. Azure Functions Specific

#### Function Configuration
- [ ] **HTTP Triggers**: Verify correct HTTP methods and auth levels
- [ ] **Function Attributes**: Check proper use of `[Function]` and trigger attributes
- [ ] **Runtime Version**: Ensure .NET 8 isolated worker model is used correctly
- [ ] **Dependency Injection**: Verify proper DI registration in `Program.cs`

#### Performance & Reliability
- [ ] **Resource Usage**: Check for efficient resource utilization
- [ ] **Async Operations**: Ensure proper handling of asynchronous operations
- [ ] **Memory Management**: Look for potential memory leaks or excessive allocations
- [ ] **Dispose Patterns**: Verify proper disposal of IDisposable resources

### 4. Infrastructure as Code (Bicep)

#### Template Quality
- [ ] **Parameter Validation**: Verify input parameters have proper constraints
- [ ] **Resource Naming**: Check consistent and meaningful resource naming
- [ ] **Tags**: Ensure proper resource tagging for cost management
- [ ] **Dependencies**: Verify correct resource dependencies

#### Security Configuration
- [ ] **Network Security**: Review NSG rules and network isolation
- [ ] **Identity Configuration**: Verify managed identity assignments
- [ ] **Storage Security**: Check storage account access policies
- [ ] **Monitoring**: Ensure Application Insights is properly configured

### 5. GitHub Actions Workflows

#### Workflow Security
- [ ] **OIDC Authentication**: Verify proper OIDC configuration
- [ ] **Permissions**: Check that workflow permissions follow least-privilege
- [ ] **Environment Protection**: Ensure production environments have protection rules
- [ ] **Secrets Usage**: Verify secrets are accessed securely and not logged

#### Workflow Efficiency
- [ ] **Parallel Execution**: Check for opportunities to parallelize jobs
- [ ] **Caching**: Verify proper NuGet package caching
- [ ] **Conditional Execution**: Ensure workflows run only when necessary
- [ ] **Resource Usage**: Review runner requirements

```yaml
# Good Example - Proper permissions
permissions:
  id-token: write
  contents: read

jobs:
  build:
    if: github.event_name == 'pull_request' && !github.event.pull_request.draft
```

### 6. Testing & Validation

#### Test Coverage
- [ ] **Unit Tests**: Verify appropriate xUnit/NUnit tests exist
- [ ] **Integration Tests**: Check end-to-end validation scenarios
- [ ] **Build Validation**: Ensure `dotnet build` completes without errors
- [ ] **Error Scenarios**: Verify proper testing of failure conditions

### 7. Documentation

#### Code Documentation
- [ ] **README Updates**: Verify README reflects any functional changes
- [ ] **XML Comments**: Check public members have documentation comments
- [ ] **API Documentation**: Update endpoint documentation
- [ ] **Architecture Diagrams**: Update diagrams if architecture changes

## 🚀 Best Practices

### Development Workflow
1. **Feature Branches**: Always create feature branches from `main`
2. **Small PRs**: Keep pull requests focused and reasonably sized
3. **Descriptive Commits**: Use clear, descriptive commit messages
4. **Self-Review**: Review your own PR before requesting reviews

### Code Quality
1. **Nullable Reference Types**: Enable and properly annotate nullable types
2. **Immutable Patterns**: Prefer immutable data patterns where appropriate
3. **Comprehensive Logging**: Use structured logging for troubleshooting
4. **Configuration Management**: Use IConfiguration for all settings

### Security First
1. **Zero Secrets**: Never commit secrets, passwords, or API keys
2. **Least Privilege**: Use minimal required permissions
3. **Audit Trail**: Ensure all operations are logged and traceable
4. **Regular Updates**: Keep NuGet packages and runtime versions current

## 🔧 Common Issues to Watch For

### .NET Specific
- **Null Reference Exceptions**: Missing null checks on nullable types
- **Async Deadlocks**: Blocking on async code (.Result, .Wait())
- **Memory Leaks**: Not disposing HttpClient or other IDisposable resources
- **Configuration Errors**: Incorrect binding or missing configuration values

### Azure Functions
- **Cold Start**: Functions not optimized for cold start scenarios
- **Timeout Issues**: Functions exceeding timeout limits
- **Dependency Injection**: Incorrect service lifetimes (Scoped vs Singleton)
- **Configuration Errors**: Incorrect `host.json` settings

## 📝 Review Comments Template

### Approval Comments
```
✅ **LGTM** - Excellent implementation
- Security: Proper managed identity usage
- Code quality: Clean, well-documented C#
- Testing: Comprehensive validation coverage
- Documentation: Updated and accurate
```

### Request Changes Comments
```
🔄 **Changes Requested**

**Security Issues:**
- [ ] Remove hardcoded connection string on line 42
- [ ] Add proper error handling for authentication

**Code Quality:**
- [ ] Add null checks for nullable parameters
- [ ] Improve error messages for troubleshooting
```

## 🎯 Final Review Checklist

Before approving any PR, ensure:
- [ ] All automated checks are passing
- [ ] Security review is complete
- [ ] Code follows project standards
- [ ] Documentation is updated
- [ ] Tests provide adequate coverage
- [ ] No breaking changes without proper communication
- [ ] Performance impact is acceptable
- [ ] Error handling is comprehensive

Remember: **It's better to ask questions than to let issues slip through!**
