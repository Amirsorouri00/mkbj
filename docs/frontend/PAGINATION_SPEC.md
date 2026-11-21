# Admin Pagination and URL State

## Product lists

Product administration uses the backend's page-based contract. The browser URL is the source of truth:

| Parameter | Default | Rules |
|---|---:|---|
| `q` | empty | Trim before writing; omit when empty |
| `status` | all | GraphQL `ProductStatus`; omit for all statuses |
| `page` | 1 | Positive integer; reset to 1 when search, status or page size changes |
| `pageSize` | 25 | One of 10, 25, 50 or 100 |

Malformed values fall back to safe defaults. Default values are omitted to keep links compact. Back/forward navigation and copied links must restore the same result set.

TanStack Query keys contain the normalized variables. Previous rows remain visible while another page loads, with a non-blocking updating label. The server's `totalCount` and `pageCount` are authoritative; the client never fabricates totals.

Products, brands, and attributes share one offset-pagination component. It provides 10/25/50/100 page sizes, direct page entry, and first/previous/next/last controls. Direct entry is clamped to the authoritative server page count. Each page corrects a URL that points beyond the last available page after receiving the response.

## Cursor lists

Category search uses its distinct cursor contract and the shared cursor-pagination component. It retains opaque cursors exactly and uses `hasNextPage`/`hasPreviousPage`; cursors are never parsed or converted into page numbers. Changing search, status, or page size clears the cursor and restarts forward navigation.

Offset and cursor controls deliberately share visual and accessibility behavior without pretending their navigation models are interchangeable. Both are semantic navigation regions, use visible labels, expose 44 px controls, and preserve the complete effective state in the URL.

## Mutations

After a mutation, invalidate the smallest matching query family and refetch the affected detail. The current catalog contract has no concurrency token, so the UI must disclose that edits are last-write-wins until the backend exposes conditional writes.
