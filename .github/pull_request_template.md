## Summary

<!-- Describe what this pull request changes. -->

## Testing

- [ ] `dotnet build src/ArchiveChainTool/ArchiveChainTool.csproj -c Release`
- [ ] `dotnet run --project src/ArchiveChainTool/ArchiveChainTool.csproj -c Release -- --self-test`
- [ ] Manual Windows UI verification

## Security checklist

- [ ] No real passwords or `data/passwords.json` included
- [ ] No user archives, extracted content, or logs included
- [ ] No password appears in command-line arguments or logs
