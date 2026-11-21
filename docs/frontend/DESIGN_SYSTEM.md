# MKBJ Admin Design System

The admin interface uses a restrained enterprise visual language: white working surfaces, charcoal navigation and a focused orange accent. `MKBJ` is a typographic wordmark until official artwork exists.

## Principles

1. **Clarity before decoration.** Dense operational data remains scannable through alignment, hierarchy and whitespace rather than ornamental cards.
2. **Actions reflect the contract.** A control is enabled only when a verified backend operation and permission exist.
3. **Status never relies on colour alone.** Badges combine text, tone and a marker.
4. **Keyboard use is complete.** Every interactive target is reachable, focus is visible, and the page starts with a skip link.
5. **Responsive does not mean losing semantics.** Tables remain tables and gain a labelled horizontal scroll region on narrow screens.

## Foundation

- Typography: system sans-serif stack, compact body type, strong but restrained headings.
- Primary accent: burnt orange `#c2410c`; hover `#9a3412`.
- Navigation: near-black `#161616`; content background `#fafafa`; surface white.
- Interactive targets: normally 44 px minimum. Compact controls are reserved for low-risk table actions and remain keyboard accessible.
- Corners: 6–12 px; cards use borders and a minimal shadow.
- Motion: short colour/state transitions only. All animation respects `prefers-reduced-motion`.

The canonical tokens are in `packages/design-tokens/src/admin.css`. Components must consume semantic variables such as `--primary`, `--border` and `--muted-foreground`, rather than duplicating colour values.

## Page structure

- Desktop: fixed 248 px sidebar, 64 px top bar, fluid content up to 1600 px.
- Mobile: navigation becomes a modal drawer and the account label collapses to an accessible account-menu trigger.
- A page begins with breadcrumbs followed by a clear eyebrow, one `h1`, supporting copy and optional actions.
- Primary work is grouped into bordered panels. Nested cards should be avoided.

## Feedback and forms

- Loading states announce themselves with `role="status"`.
- Request failures use an inline alert, preserve the server correlation reference, and offer retry when safe.
- Permission failures explain the restriction and provide a route home.
- Empty states describe the next useful action, including clearing filters.
- Mutation forms must provide a focusable error summary plus inline field errors. Validation cannot be toast-only.

## Tables

- Use native `table`, `thead`, `tbody`, row headers and column scopes.
- The product name is the primary row link; the trailing action repeats it with an explicit accessible name.
- Pagination remains outside the scroll region.
- Offset pagination provides direct page entry plus first/previous/next/last actions; cursor pagination exposes only verified previous/next capabilities.
- Fetching the next result preserves current rows and exposes an “Updating…” label to prevent disruptive flashing.

## Accessibility acceptance

- Logical heading structure and landmark names.
- Visible 2 px focus ring with offset.
- 4.5:1 text contrast for ordinary text.
- No colour-only state, hover-only disclosure or inaccessible custom controls.
- Keyboard path tested for sign-in, navigation, search, filters, rows and pagination.
