# devbridge9m test focus

1. Local My Lamb server must never request `/__API_BASE__/...`; local frontend rewrites both minified and spaced API_BASE placeholders to same-origin.
2. Runtime follower preview extraction:
   - direct SkinAndData sprite fields first
   - loaded Unity Sprite fallback scored by follower form id
   - SkinAndData.SlotAndColours recursive palette extraction for real game colors
   - nested SlotAndColours diagnostics if variants/previews remain unresolved
3. Expected local server path: `GET /streamers/<id>/catalog` (no `__API_BASE__`).
4. Expected catalog diagnostics may show non-zero `previewForms` and `paletteColors`. If previewForms remains zero, copy the `[PREVIEW DIAG]` line; it now includes the first SlotAndColours item type/members.
