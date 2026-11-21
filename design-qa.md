# MKBJ storefront and backoffice design QA

## Evidence

- Source visual truth: `C:\Users\Daniyal\AppData\Local\Temp\codex-clipboard-26e31c02-1332-4c10-85aa-c442ee95a2d3.png`
- Combined source/implementation comparison: `C:\dev\mkbj-chatgpt\docs\frontend\qa-comparison.png`
- Storefront home, desktop: `C:\dev\mkbj-chatgpt\docs\frontend\qa-home-desktop.png`
- Storefront PLP, desktop: `C:\dev\mkbj-chatgpt\docs\frontend\qa-plp-desktop.png`
- Storefront home, mobile: `C:\dev\mkbj-chatgpt\docs\frontend\qa-home-mobile.png`
- Storefront PLP, mobile: `C:\dev\mkbj-chatgpt\docs\frontend\qa-plp-mobile.png`
- Backoffice products, desktop: `C:\dev\mkbj-chatgpt\docs\frontend\qa-admin-desktop.png`

The supplied 1sell material is a design-system reference rather than a pixel-identical screen specification. The comparison therefore evaluates palette, information density, hierarchy, alignment, commerce patterns, and responsive behavior while preserving the MKBJ identity and its real data model.

## Capture normalization

- Source pixels: 1178 × 2560, including mobile browser chrome and a portfolio canvas.
- Desktop implementation pixels: 1265 × 712. Browser CSS viewport reported 1280 px wide; screenshot density was 1×.
- Mobile PLP implementation pixels: 375 × 811. Browser CSS viewport was 390 × 844; screenshot density was 1× and browser chrome was excluded.
- Combined comparison pixels: 2000 × 1300. The source was aspect-fitted on the left; desktop home and PLP captures were independently aspect-fitted on the right. No fidelity judgment was based on browser chrome, the portfolio canvas, or density differences.
- States: anonymous shop home in USD, a `headphone` PLP sorted price-low-to-high, collapsed and expanded mobile filters, and authenticated backoffice product listing.

## Full-view comparison

The implementation carries over the reference's charcoal commerce header, warm orange action color, neutral white/soft-gray surfaces, compact product merchandising, clearly separated category navigation, and disciplined horizontal alignment. MKBJ uses a denser desktop PLP, reusable product rails, and a larger editorial home hero appropriate to its current catalogue rather than duplicating 1sell's exact product-detail screen.

The storefront showed eight available category tiles, forty product cards across the backend-composed home sections, three category-specific product lines plus new arrivals, and live product media. The backoffice uses the same charcoal/orange/neutral token family without losing the clearer operational table layout.

## Focused-region comparison

- Header and navigation: charcoal shell, white MKBJ wordmark, orange search action, account/cart actions, and a category row match the reference system's hierarchy.
- Product cards: 4:3 contained media, compact brand/title/price hierarchy, and restrained borders reduce the oversized-card problem visible in the original PLP capture.
- Mobile listing: the desktop filter sidebar becomes a single accessible disclosure. With filters collapsed, two product columns enter the first viewport; expanding the control exposes the complete filter form.
- Imagery: sampled listing images reported `naturalWidth: 600`; the live media URL returned HTTP 200 with `image/png`. The demo seed files are intentionally abstract color swatches, while production media will render through the same component and object-storage path.
- Backoffice palette: sidebar, selected navigation, primary action, borders, and page background all use the shared charcoal/warm-orange/neutral system.

## Required fidelity surfaces

- Fonts and typography: Rubik-led commerce typography preserves the reference's friendly geometric weight, with clear display/body/metadata separation and no visible clipping at checked breakpoints.
- Spacing and layout rhythm: desktop PLP is three to four product columns beside a fixed-width filter rail depending on viewport; mobile is two columns with a collapsed filter control. Section, card, and header alignment remain consistent.
- Colors and visual tokens: storefront and backoffice share charcoal, orange, warm off-white, neutral border, and semantic status tokens. Primary shop actions use dark text on orange for contrast.
- Image quality and asset fidelity: the broken URL issue is resolved and real stored assets load. Demo seed imagery remains abstract by dataset design, not as a frontend fallback.
- Copy and content: commerce copy is concise, MKBJ-specific, and does not expose implementation language. Category copy states that only currently available categories are shown.
- Accessibility and interaction: skip link, semantic headings, labeled search/sort controls, keyboard-reachable links, visible focus styles, filter `aria-expanded`, reduced-motion handling, and responsive overflow were checked. Desktop and mobile `scrollWidth` stayed within `innerWidth`.

## Findings

No actionable P0, P1, or P2 findings remain.

- [P3] Replace abstract demo swatches with representative catalogue photography when production-quality seed content becomes available. This is a data/content refinement; the media delivery and rendering path is working.

## Comparison history

### Pass 1 — blocked

- [P1] Mobile PLP exposed the full desktop filter form before the product grid, consuming the first screen and delaying product discovery.
- Fix: added the reusable `FilterPanel` disclosure component, collapsed on tablet/mobile and unchanged as a sticky sidebar on desktop.
- Post-fix evidence: `docs/frontend/qa-plp-mobile.png` shows the compact control and product cards in the first viewport. The expanded state exposed the form with `aria-expanded="true"` and a visible filter body.

### Pass 2 — passed

- Product cards were visible in the first mobile viewport.
- Fourteen of fourteen sampled PLP images loaded with nonzero dimensions.
- No horizontal overflow was present on checked desktop or mobile states.
- Storefront and backoffice browser consoles contained no errors or warnings.
- No P0, P1, or P2 differences remained after accounting for the source being a design-system reference rather than a screen to clone.

## Primary interactions tested

- Home and PLP server rendering against the live GraphQL API.
- Currency-preserving product links and section links.
- Responsive mobile navigation state.
- Mobile filter disclosure collapsed and expanded states.
- Product image loading from MinIO's public catalogue bucket.
- Backoffice secure-session restoration and products screen rendering.
- Browser console error/warning inspection for shop and backoffice.

## Implementation checklist

- [x] Shared charcoal/orange/neutral design tokens for shop and backoffice.
- [x] Reusable product image, product card, product rail, category card, commerce hero, section heading, and filter disclosure components.
- [x] Backend-composed homepage section limits and category product lines.
- [x] Category tree pruned to categories backed by available storefront products.
- [x] Public media base URL configured for development and production compose.
- [x] Compact responsive PLP and mobile filter behavior.
- [x] Browser-rendered desktop/mobile evidence and clean console checks.

final result: passed
