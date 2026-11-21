# MKBJ Shop Design System

## Direction

The customer storefront uses a trustworthy, product-first marketplace language with balanced visual variance, restrained motion and compact retail density. Shop and Backoffice share one MKBJ palette while retaining different information density and navigation patterns.

The system is informed by the supplied 1sell references: charcoal commerce chrome, warm orange actions, white merchandise surfaces, compact repeated product rows and disciplined alignment. MKBJ retains its own clean typographic wordmark, content and component structure rather than copying the referenced brand.

## Foundation

- Commerce accent: `#F28C00`; hover: `#D97700`; soft surface: `#FFF2DC`.
- Chrome: `#1C1C1C`; secondary chrome: `#2A2A2A`.
- Background: `#F5F5F3`; product surfaces: white; text: `#181818`; muted: `#606060`.
- Supporting accents: red `#C9362B`, yellow `#F5B700`, green `#187A50`.
- Typography: Rubik for display headings, Nunito Sans for interface and body content, with system fallbacks.
- Radius: 6–14px, based on hierarchy. Product cards use a border and restrained elevation.
- Motion: 150–250ms state transitions, transform/opacity only, with `prefers-reduced-motion` respected.

## Commerce rules

- Product imagery and price lead; decoration never competes with the buying decision.
- One dominant action per view. Secondary actions remain visibly subordinate.
- Search is always reachable from the header.
- Currency is explicit and URL-persisted. Money remains a two-decimal server string.
- Product cards reserve a stable 4:3 image area, use contain-fit product imagery, and target six columns on wide homepage rails and five columns beside desktop PLP filters. This density keeps product comparison fast without reducing mobile touch targets.
- Homepage product lines and category visibility are supplied by the backend; the browser does not invent unavailable departments.
- Product option selection uses labelled controls with at least 44px targets.
- Cart price changes are explained inline and require the customer to confirm the refreshed total.
- Empty, loading, error and authentication states always provide a recovery action.

## Responsive and accessible behavior

- Mobile-first at 375px, then 768px, 1024px and 1440px.
- The desktop category bar becomes a labelled drawer on small screens.
- Body text is at least 16px on mobile; controls are at least 44px high.
- All interactive controls have visible focus, descriptive names and keyboard behavior.
- Colour is never the only state indicator. Ordinary text meets 4.5:1 contrast.
- No hover-only content, horizontal page scrolling, or disabled zoom.
