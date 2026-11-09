# GraphQL API Reference

Endpoint: `/graphql` (all environments)

In Development, **Banana Cake Pop** (HotChocolate's built-in GraphQL IDE) is available at the same URL in a browser — use it to explore the schema, run queries, and test mutations with auto-complete.

## Authentication

Auth is **per resolver**, not per schema. Each resolver carries `[Authorize(Policy = ...)]` then `[RequirePermission(...)]`.

| Surface | Auth | Example resolvers |
|---------|------|-------------------|
| Shop anonymous | None | `shopHome`, `shopCategories`, `shopProducts`, `shopSearch` |
| Shop customer | `shop:customer` | `shopUpdateProfile`, `shopAddToCart`, `shopCheckout` |
| Admin staff | `admin:staff` + permission | `adminDashboard`, `adminOrders`, `adminCustomers` |
| Admin super | `admin:super` + permission | `adminPartners`, `adminSettings`, `adminRefund` |

Pass the token as: `Authorization: Bearer <token>` in the HTTP headers panel of Banana Cake Pop.

## Schema Limits

- Max query depth: **10**
- Max field/type cost: **20,000**

Exceeding either returns a validation error before execution.

## Key Resolvers by Module

### Catalog (Admin)

| Type | Name | Auth | Description |
|------|------|------|-------------|
| Mutation | `createProduct` | staff + catalog.manage | Create product |
| Mutation | `updateProduct` | staff + catalog.manage | Update product |
| Mutation | `publishProduct` | staff + catalog.manage | Publish product |
| Mutation | `createBrand` | staff + catalog.manage | Create brand |
| Mutation | `updateBrand` | staff + catalog.manage | Update brand |
| Mutation | `createCategory` | staff + catalog.manage | Create category |
| Mutation | `updateCategory` | staff + catalog.manage | Update category |
| Mutation | `createAttributeBlueprint` | staff + catalog.manage | Create attribute |
| Mutation | `assignCategoryAttribute` | staff + catalog.manage | Assign attribute to category |
| Query | `categoryAttributeAssignments` | staff + catalog.manage | List assignments |
| Query | `categoryAttributeExclusions` | staff + catalog.manage | List exclusions |

### Customer

| Type | Name | Auth | Description |
|------|------|------|-------------|
| Mutation | `shopUpdateProfile` | shop:customer | Update own profile |
| Mutation | `adminUpdateCustomer` | staff + customers.manage | Update any customer |

### Partners

| Type | Name | Auth | Description |
|------|------|------|-------------|
| Query | `adminPartners` | super + partners.manage | List partners |
| Query | `adminPartner` | super + partners.manage | Partner detail |
| Mutation | `adminCreatePartner` | super + partners.manage | Create partner |
| Mutation | `adminUpdatePartner` | super + partners.manage | Update partner |
| Mutation | `adminIssuePartnerKey` | super + partners.manage | Issue API key |
| Mutation | `adminRevokePartnerKey` | super + partners.manage | Revoke API key |

### Pricing

| Type | Name | Auth | Description |
|------|------|------|-------------|
| Query | `currencies` | anonymous | Enabled currencies |
| Query | `adminPricingVariant` | staff + catalog.manage | Variant price info |
| Query | `adminFxRates` | staff + catalog.manage | FX rates |
| Mutation | `adminSetReferencePrice` | staff + catalog.manage | Set price |
| Mutation | `adminSetPriceOverride` | staff + catalog.manage | Currency override |
| Mutation | `adminCreateFxOverride` | super + fx.manage | FX override |
| Mutation | `adminEnableCurrency` | super + fx.manage | Enable/disable currency |

### Sales

| Type | Name | Auth | Description |
|------|------|------|-------------|
| Mutation | `shopAddToCart` | shop:customer | Add item to cart |
| Mutation | `shopUpdateCartItem` | shop:customer | Update cart item |
| Mutation | `shopRemoveCartItem` | shop:customer | Remove cart item |
| Mutation | `shopClearCart` | shop:customer | Clear cart |
| Mutation | `shopCheckout` | shop:customer | Submit order |
| Mutation | `adminConfirmPayment` | staff + orders.manage | Confirm payment |
| Mutation | `adminIssueInvoice` | staff + orders.manage | Issue invoice |
| Mutation | `adminCancelOrder` | staff + orders.manage | Cancel order |
| Mutation | `adminRefundOrder` | super + refunds.manage | Issue refund |

### Settings

| Type | Name | Auth | Description |
|------|------|------|-------------|
| Query | `adminSellerProfile` | super + settings.manage | Seller profile |
| Query | `adminPaymentInstructions` | super + settings.manage | Payment instructions |
| Query | `adminCommerceLimits` | super + settings.manage | Commerce limits |
| Mutation | `adminUpdateSellerProfile` | super + settings.manage | Update seller |
| Mutation | `adminUpdatePaymentInstructions` | super + settings.manage | Update payment instructions |
| Mutation | `adminUpdateCommerceLimits` | super + settings.manage | Update limits |

## Example Query

```graphql
query ShopHome {
  shopHome {
    featuredProducts {
      id
      name
      slug
      variants {
        id
        price { amountMinor currency }
      }
    }
    categories {
      id
      name
      slug
    }
  }
}
```

## Example Mutation

```graphql
mutation AddToCart($input: ShopAddToCartInput!) {
  shopAddToCart(input: $input) {
    cart {
      items {
        variantId
        quantity
        lineTotal { amountMinor currency }
      }
      total { amountMinor currency }
    }
  }
}
```

Variables:
```json
{
  "input": {
    "variantId": "123456789",
    "quantity": 2,
    "currency": "SGD"
  }
}
```
