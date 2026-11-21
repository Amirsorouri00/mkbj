# Backend API Gaps for Shop Web

Verified against the live GraphQL schema, OpenAPI document and implemented shop application on 2026-11-13. Proposed shapes are recommendations, not current contracts.

## Priority summary

| Priority | Gap | Customer impact | Recommended backend direction |
|---|---|---|---|
| P1 | Product images have URLs but no alternative text | Meaningful product imagery cannot be described accessibly | Return structured media with `url`, `alt`, dimensions and primary status |
| P1 | Cart lines omit product slug and thumbnail | Cart cannot link back to the product or show its image without an N+1 lookup | Add a compact immutable display snapshot to each cart line |
| P1 | Checkout/order payment instructions are not a dedicated typed field | Confirmation cannot present authoritative payment steps cleanly | Return a versioned typed payment-instruction snapshot with the placed order |
| P1 | No anonymous seller/contact/policy contract | Help and footer content cannot use authoritative seller details | Add a public, deliberately limited shop-information query |
| P1 | Home cards can retain a converted display price after its FX validity expires | A customer can open a priced card and find no currently priced variant | Apply the `fx_valid_until` guard consistently to home/product projections or refresh them before expiry |
| P2 | Pagination is forward-only and has no result total | Customers cannot navigate backward except with browser history or understand set size | Add reverse cursor support; add totals only if the query cost is acceptable |
| P2 | Search and category facets cannot be requested as one contextual contract | The client makes a separate facet request and may show category-only counts beside text search | Accept the complete listing filter in the facet query or return facets with the listing page |
| P2 | No recently viewed, saved list, recommendations or stock signal | Discovery remains MVP-level | Add only after product analytics and inventory ownership are defined |
| P2 | Document `content` is an untyped `Any` scalar | A browser cannot safely render document-specific content from generated types | Add typed document summary variants while retaining raw snapshot access where needed |

## Resolved during shop implementation

Storefront GraphQL originally exposed product, category and variant Snowflake IDs as `Long`. Hot Chocolate serialized those fields as JSON numbers, which silently rounded values above JavaScript’s safe integer limit and caused a valid add-to-cart request to carry the wrong variant ID. The shop GraphQL boundary now exposes all customer-used identifiers as `ID`, emits exact strings, accepts string inputs, and covers the real authenticated cart journey in Playwright.

Order monetary minor-unit fields now serialize as decimal strings in the shop GraphQL surface. Cart totals already use the two-decimal `MoneyGraph` string contract.

The frontend treats an empty product `variants` list as a first-class unavailable state and never fabricates a purchasable price. This is safe, but it does not remove the browse-contract inconsistency: the home projection can still advertise its older converted range until the backend view is refreshed or the home query adopts the same expiry guard used by listing/detail offers.

## 1. Product media

Current cards expose one nullable `imageUrl`; product detail exposes a list of URL strings. A future contract should preserve the current simple adoption path while adding accessibility and layout data:

```graphql
type ShopProductImage {
  id: ID!
  url: String!
  alt: String
  width: Int
  height: Int
  primary: Boolean!
}
```

Cards need only a nullable primary thumbnail. Detail can return the ordered image list. The frontend already reserves square media space, so adoption is a fragment and mapper change rather than a layout rewrite.

## 2. Sorting and filtering

The current product list supports relevance, newest, popularity, ascending price and descending price. Filters support query text, category, brands, attribute values and a price range. Preserve those fields and add new options through allow-listed enum/input fields rather than arbitrary expressions.

Likely next filters are in-stock/available-now once inventory exists, price status, delivery eligibility, promotion, and multiple categories. Every ordering must append a stable unique ID tie-breaker so cursor pages cannot skip or repeat products.

The facet query should eventually accept the same complete filter input as the list query. Counts should describe the result set the customer is looking at, excluding only the facet group currently being expanded when that behavior is explicitly documented.

## 3. Cart display snapshot

`CartLineGraph` currently returns variant ID, SKU, title, quantity, unit price and line total. Add `productSlug`, nullable thumbnail metadata, selected variant attributes and brand name. These are presentation snapshots only; live Catalog and Pricing checks must remain the purchase authority.

## 4. Checkout confirmation

Checkout correctly accepts only `expectedTotal`. Keep that invariant. Enrich the successful payload instead:

```graphql
type ShopCheckoutSuccess {
  order: ShopOrderDto!
  paymentInstruction: ShopPaymentInstruction!
  proForma: ShopDocumentSummary
}

type ShopPaymentInstruction {
  version: Int!
  text: String!
}
```

The version and text must be the snapshot captured by the order, not the current Settings value read afterward.

## 5. Public shop information

Expose only deliberately public seller data: display/legal name as approved, customer-service contact, operating country, payment explanation, privacy URL and terms URL. Do not reuse the unrestricted admin Settings DTO. The shop help page currently uses safe generic wording until this contract exists.

## Adoption contract

When a proposed field arrives:

1. refresh the checked-in SDL from the live host;
2. update `packages/api-graphql/operations/shop.graphql`;
3. regenerate the typed documents;
4. extend the URL/request mapper or card/cart mapper;
5. add an exact contract test and desktop/mobile interaction coverage.

No page should add an N+1 detail request as a temporary thumbnail, cart or facet workaround.
