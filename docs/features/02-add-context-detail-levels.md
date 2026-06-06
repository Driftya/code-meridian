# Add Context Detail Levels

- Status: done
- Priority: P0
- Note: Token savings only work if every tool avoids returning too much by default


**Why:** Token savings only work if every tool avoids returning too much by default. The default should be compact, with expansion available when explicitly requested.

**Suggested enum:**

```csharp
public enum ContextDetailLevel
{
    Summary,
    Compact,
    Full
}
```

**Apply to:**

- `get_context_for_editing`
- `find_impact`
- `find_downstream`
- `find_connection`
- `find_coverage_gaps`
- `find_large_nodes`
- New `build_minimal_context`

**Rules:**

- `Summary`: names, paths, relationship types, risk score
- `Compact`: summary plus top relevant metadata
- `Full`: only when the caller explicitly asks for source snippets or expanded detail

**Effort:** Medium  
**Value:** High  
**Risk:** Medium, because output tests will need updates.

