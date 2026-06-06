# Fix Stable IDs for Top-Level Local Functions

- Status: done
- Priority: P0
- Note: Top-level local functions in different `Program.cs` files can collide because the generated method ID only uses the signature when no namespace/type exists


**Why:** Top-level local functions in different `Program.cs` files can collide because the generated method ID only uses the signature when no namespace/type exists. This caused duplicate `IsAuthorized(HttpRequest,string)` functions to collapse into one graph node.

**Tasks:**

- Include file identity or container identity in IDs for local functions.
- Preserve stable IDs for normal class methods where possible.
- Add tests for two top-level `Program.cs` files with same local function names.
- Consider a migration note: clear and re-index projects after this change.

**Effort:** Low to medium  
**Value:** High  
**Risk:** Medium, because node ID changes can affect existing graph references.

