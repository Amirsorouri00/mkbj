# Graph Report - mkbj  (2026-11-09)

## Corpus Check
- Large corpus: 1197 files · ~514,020 words. Semantic extraction will be expensive (many Claude tokens). Consider running on a subfolder.

## Summary
- 10724 nodes · 38586 edges · 314 communities (256 shown, 58 thin omitted)
- Extraction: 87% EXTRACTED · 13% INFERRED · 0% AMBIGUOUS · INFERRED: 4839 edges (avg confidence: 0.83)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- Community 0
- Community 1
- Community 2
- Community 3
- Community 4
- Community 5
- Community 6
- Community 7
- Community 8
- Community 9
- Community 10
- Community 11
- Community 12
- Community 13
- Community 14
- Community 15
- Community 16
- Community 17
- Community 18
- Community 19
- Community 20
- Community 21
- Community 22
- Community 23
- Community 24
- Community 25
- Community 26
- Community 27
- Community 28
- Community 29
- Community 30
- Community 31
- Community 32
- Community 33
- Community 34
- Community 35
- Community 36
- Community 37
- Community 38
- Community 39
- Community 40
- Community 41
- Community 42
- Community 43
- Community 44
- Community 45
- Community 46
- Community 47
- Community 48
- Community 49
- Community 50
- Community 51
- Community 52
- Community 53
- Community 54
- Community 55
- Community 56
- Community 57
- Community 58
- Community 59
- Community 60
- Community 61
- Community 62
- Community 63
- Community 64
- Community 65
- Community 66
- Community 67
- Community 68
- Community 69
- Community 70
- Community 71
- Community 72
- Community 73
- Community 74
- Community 75
- Community 76
- Community 77
- Community 78
- Community 79
- Community 80
- Community 81
- Community 82
- Community 83
- Community 84
- Community 85
- Community 86
- Community 87
- Community 88
- Community 89
- Community 90
- Community 91
- Community 92
- Community 93
- Community 94
- Community 95
- Community 96
- Community 97
- Community 98
- Community 99
- Community 100
- Community 101
- Community 102
- Community 103
- Community 104
- Community 105
- Community 106
- Community 107
- Community 108
- Community 109
- Community 110
- Community 111
- Community 112
- Community 113
- Community 114
- Community 115
- Community 116
- Community 117
- Community 118
- Community 119
- Community 120
- Community 121
- Community 122
- Community 123
- Community 124
- Community 125
- Community 126
- Community 127
- Community 128
- Community 129
- Community 130
- Community 131
- Community 132
- Community 133
- Community 134
- Community 135
- Community 136
- Community 137
- Community 138
- Community 139
- Community 140
- Community 141
- Community 142
- Community 143
- Community 144
- Community 145
- Community 146
- Community 147
- Community 148
- Community 149
- Community 150
- Community 151
- Community 152
- Community 153
- Community 154
- Community 155
- Community 156
- Community 157
- Community 158
- Community 159
- Community 160
- Community 161
- Community 162
- Community 163
- Community 164
- Community 165
- Community 166
- Community 167
- Community 168
- Community 169
- Community 170
- Community 171
- Community 172
- Community 173
- Community 174
- Community 175
- Community 176
- Community 177
- Community 178
- Community 179
- Community 180
- Community 181
- Community 182
- Community 183
- Community 184
- Community 185
- Community 186
- Community 187
- Community 188
- Community 189
- Community 190
- Community 191
- Community 192
- Community 193
- Community 194
- Community 195
- Community 196
- Community 197
- Community 198
- Community 199
- Community 200
- Community 201
- Community 202
- Community 203
- Community 204
- Community 205
- Community 206
- Community 207
- Community 208
- Community 209
- Community 210
- Community 211
- Community 212
- Community 213
- Community 214
- Community 215
- Community 216
- Community 217
- Community 218
- Community 219
- Community 220
- Community 221
- Community 222
- Community 223
- Community 224
- Community 225
- Community 226
- Community 227
- Community 228
- Community 229
- Community 230
- Community 231
- Community 232
- Community 233
- Community 234
- Community 235
- Community 236
- Community 237
- Community 238
- Community 239
- Community 240
- Community 241
- Community 242
- Community 243
- Community 244
- Community 245
- Community 246
- Community 247
- Community 248
- Community 249
- Community 250
- Community 251
- Community 252
- Community 253
- Community 254
- Community 255
- Community 256
- Community 257
- Community 258
- Community 259
- Community 260
- Community 261
- Community 262
- Community 263
- Community 264
- Community 265
- Community 266
- Community 267
- Community 268
- Community 269
- Community 270
- Community 272
- Community 273
- Community 274
- Community 275
- Community 276
- Community 277
- Community 278
- Community 279
- Community 280
- Community 281
- Community 282
- Community 283
- Community 284
- Community 285
- Community 286
- Community 287
- Community 288
- Community 289
- Community 290
- Community 291
- Community 292
- Community 294
- Community 295
- Community 296
- Community 297
- Community 298
- Community 299
- Community 300
- Community 301
- Community 302
- Community 303
- Community 304
- Community 305
- Community 306
- Community 307
- Community 308
- Community 309
- Community 310
- Community 311

## God Nodes (most connected - your core abstractions)
1. `Result` - 528 edges
2. `DomainException` - 247 edges
3. `EwaysVNextDbContext` - 213 edges
4. `EwaysVNext.Outcomes` - 177 edges
5. `EwaysVNext.Domain.Common` - 171 edges
6. `AdminActor` - 151 edges
7. `EwaysVNext.Database` - 107 edges
8. `Order` - 99 edges
9. `EwaysVNext.Core.Application` - 97 edges
10. `Category` - 97 edges

## Surprising Connections (you probably didn't know these)
- `14 release checks` --conceptually_related_to--> `Phase 8: Ops, Smoke, Load & Docs`  [INFERRED]
  tools/smoke/smoke.sh → docs/reports/phase1-8/README.md
- `roles super_admin, staff` --conceptually_related_to--> `Admin module`  [INFERRED]
  build/keycloak/Backoffice-realm.json → docs/superpowers/specs/2026-10-28-ecommerce-mvp-design.md
- `Jobs__Mode (api Disabled, worker runs jobs)` --implements--> `Postgres-backed background jobs`  [INFERRED]
  build/compose/docker-compose.yml → docs/superpowers/specs/2026-10-28-ecommerce-mvp-design.md
- `verifyEmail + self-registration` --conceptually_related_to--> `Website order flow`  [INFERRED]
  build/keycloak/Shop-realm.json → docs/superpowers/specs/2026-10-28-ecommerce-mvp-design.md
- `Backoffice realm export` --implements--> `Humans: Keycloak realms`  [EXTRACTED]
  build/keycloak/Backoffice-realm.json → docs/superpowers/specs/2026-10-28-ecommerce-mvp-design.md

## Import Cycles
- None detected.

## Communities (314 total, 58 thin omitted)

### Community 0 - "Community 0"
Cohesion: 0.03
Nodes (64): EwaysVNext.Modules.Catalog.Application.Categories.Domain, EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Domain, EwaysVNext.Modules.Catalog.Adapter.Outbound.Repositories.Brands, EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Validation, EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.AttributeBlueprints, EwaysVNext.Modules.Catalog.QueryApplication, EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound, EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound (+56 more)

### Community 1 - "Community 1"
Cohesion: 0.02
Nodes (66): EwaysVNext.Modules.Customer.Adapter.Inbound.Rest, EwaysVNext.IntegrationTests.Host, EwaysVNext.Host, EwaysVNext.Modules.Partners.Adapter.Inbound.Rest.Authentication, EwaysVNext.Modules.Sales.Adapter.Inbound.Rest.Admin, EwaysVNext.Modules.Admin.Adapter.Inbound.Rest, EwaysVNext.Core.Application, EwaysVNext.Presentation.Endpoint.Extensions (+58 more)

### Community 2 - "Community 2"
Cohesion: 0.02
Nodes (81): EwaysVNext.Outcomes.Extensions, AuthorizationService.Application.Scopes.Responses, EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.CategoryAttributeExclusion, AuthorizationService.Implementation.Services, EwaysVNext.Modules.Partners.Adapter.Inbound.GraphQL, AuthorizationService.Application.UserRoleScopes.Requests, AuthorizationService.Application.Roles, AuthorizationService.Application.Roles.Requests (+73 more)

### Community 3 - "Community 3"
Cohesion: 0.03
Nodes (28): AuditActor, System, IAuditTrail, AdminActor, IUnitOfWork, IBrandAdminService, BrandAdminService, ICatalogChangeNotifier (+20 more)

### Community 4 - "Community 4"
Cohesion: 0.03
Nodes (13): IObjectStorage, S3ObjectStorage, UnavailableAttributeBlueprintDependencyGateway, StorefrontViewRefresher, PricingAdminGraphQLMutations, IPricingAdminService, ObjectStorageDocumentStore, OrderRepository (+5 more)

### Community 5 - "Community 5"
Cohesion: 0.03
Nodes (37): IPermissionService, CreatePermissionRequest, PermissionRuleRequest, UpdatePermissionRequest, PermissionDto, PermissionRuleDto, PermissionRuleScopeDto, IRoleService (+29 more)

### Community 6 - "Community 6"
Cohesion: 0.06
Nodes (31): AssignCategoryProductCommand, BulkDeactivateCategoriesCommand, CreateCategoryCommand, DeleteCategoryCommand, MoveCategoryCommand, SetCategoryStatusCommand, UnassignCategoryProductCommand, UpdateCategoryCommand (+23 more)

### Community 7 - "Community 7"
Cohesion: 0.04
Nodes (26): CatalogVariantGateway, PricingChangeNotifier, PricingPolicyGateway, CurrencyRepository, FxRateOverrideRepository, VariantPriceRepository, Full, ICurrencyRepository (+18 more)

### Community 8 - "Community 8"
Cohesion: 0.04
Nodes (21): EfCoreRepository, Context, Set, CatalogProductLookup, CategoryProductRepository, ProductRepository, Full, MeasurementUnitRepository (+13 more)

### Community 9 - "Community 9"
Cohesion: 0.04
Nodes (33): EwaysVNext.Modules.Pricing.Application.Fx.Ports.Outbound, EwaysVNext.Modules.Pricing.Application.Currencies.Ports.Outbound, EwaysVNext.Outcomes, EwaysVNext.Modules.Catalog.Application.Storefront.Ports.Inbound, EwaysVNext.Modules.Pricing.Application.Prices.Domain, EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound, EwaysVNext.Modules.Pricing.Application.Currencies.Domain, EwaysVNext.Modules.Pricing.Application (+25 more)

### Community 10 - "Community 10"
Cohesion: 0.04
Nodes (27): Init, AdminPermissionMatrix, AuditVerifyGrantSeed, Init, Partners, PartnerUsage, Settings, CustomerRebuild (+19 more)

### Community 11 - "Community 11"
Cohesion: 0.04
Nodes (16): IStorefrontViewRefresher, CatalogAdminRestTests, ShopProfileTests, WorkerOnlyHostTests, ErrorBodyEndToEndTests, EndpointAuthorizationTests, Ct, SettingsAdminApiTests (+8 more)

### Community 12 - "Community 12"
Cohesion: 0.03
Nodes (12): Result, Value, BulkDeactivateCategoriesInput, CategoryAdminMutations, CategoryIdInput, ICategoryService, UsdConversion, CandidatePool (+4 more)

### Community 13 - "Community 13"
Cohesion: 0.03
Nodes (43): EwaysVNext.Modules.Partners.Application.Partners.Domain, EwaysVNext.Modules.Settings, EwaysVNext.Modules.Settings.Adapter.Outbound, EwaysVNext.Modules.Catalog.Contracts.Categories, AuthorizationService.Implementation.Persistence, EwaysVNext.Modules.Settings.Adapter.Inbound.Rest.Admin, EwaysVNext.Modules.Partners, EwaysVNext.Modules.Partners.Adapter.Outbound.Persistence (+35 more)

### Community 14 - "Community 14"
Cohesion: 0.03
Nodes (30): CustomerRepository, InvoiceParty, PartnerCustomerRef, ICustomerRepository, CustomerProfileService, CustomerResolutionService, CachedCandidatePoolGateway, SalesCustomerGateway (+22 more)

### Community 15 - "Community 15"
Cohesion: 0.05
Nodes (23): EwaysVNext.Modules.Sourcing.QueryApplication, EwaysVNext.IntegrationTests.Storefront, EwaysVNext.IntegrationTests.Settings, EwaysVNext.IntegrationTests.Catalog, EwaysVNext.IntegrationTests.Platform, EwaysVNext.Modules.Partners.Application.Usage, EwaysVNext.Jobs.Implementation, EwaysVNext.IntegrationTests.Contracts (+15 more)

### Community 16 - "Community 16"
Cohesion: 0.04
Nodes (13): BuildDirectoryTests, Root, DocumentationTests, Root, AttributeBlueprintCanonicalTests, CategoryProductionCompositionTests, CategoryQueryTests, QueryExecutionCounter (+5 more)

### Community 17 - "Community 17"
Cohesion: 0.04
Nodes (33): EwaysVNext.UnitTests.Sales.Services, EwaysVNext.UnitTests.Sales.Gateways, EwaysVNext.Modules.Sales.Adapter.Inbound.Rest, EwaysVNext.Tools.CompositionSimulator, EwaysVNext.Modules.Sales.Application.Domain.Orders, EwaysVNext.Modules.Sales.QueryApplication.Partner, EwaysVNext.Modules.Sales.Application.Ports.Inbound, EwaysVNext.Modules.Sales.Application.Domain.Carts (+25 more)

### Community 18 - "Community 18"
Cohesion: 0.05
Nodes (41): DomainEvent, EventId, OccurredOn, AttributeBlueprintConfiguration, AttributeBlueprints, AttributeBlueprint, Aliases, CanonicalKey (+33 more)

### Community 19 - "Community 19"
Cohesion: 0.04
Nodes (28): CategoryDto, CategoryProductPageDto, CategoryTreeNodeDto, AttributeFilterInput, ShopProductFilterInput, ShopQueries, StatusTransition, ICategoryQueries (+20 more)

### Community 20 - "Community 20"
Cohesion: 0.04
Nodes (13): MoneyText, PriceBand, AmountText, GtinValidator, PriceParser, Noise, PhoneTests, JobBackoffTests (+5 more)

### Community 21 - "Community 21"
Cohesion: 0.07
Nodes (13): AdminGraphQLClient, GraphQLError, GraphQLErrorExtensions, GraphQLRequest, GraphQLResponse, CatalogAdminGraphQLTests, AdminRowShape, AssignmentShape (+5 more)

### Community 22 - "Community 22"
Cohesion: 0.05
Nodes (33): IDomainEvent, EventId, OccurredOn, IDomainEventBus, IEvent, EfCoreUnitOfWork, ShopCartEndpoints, CartChange (+25 more)

### Community 23 - "Community 23"
Cohesion: 0.06
Nodes (48): CreatePartnerOrderCommand, PartnerContact, PartnerOrderKey, CountingPricing, LastCurrency, LiveReferenceShift, PartnerOrderServiceTests, Ct (+40 more)

### Community 24 - "Community 24"
Cohesion: 0.05
Nodes (22): DomainException, Code, ProductIdentifierConfiguration, AddProductIdentifierInput, CategoryActivatedDomainEvent, CategoryDeletedDomainEvent, CategoryMovedDomainEvent, Gs1 (+14 more)

### Community 25 - "Community 25"
Cohesion: 0.05
Nodes (45): ImportJobs, ImportJobConfiguration, CreateImportJobRequest, ImportJob, AcceptsWork, Checkpoint, Counters, CreatedAt (+37 more)

### Community 26 - "Community 26"
Cohesion: 0.05
Nodes (23): FxRateOverrides, FxRates, FxRateConfiguration, FxRateOverrideConfiguration, FxRate, Base, FetchedAt, Provider (+15 more)

### Community 27 - "Community 27"
Cohesion: 0.07
Nodes (12): Program, IImportPipeline, ISourcingAdminQueries, StagedRecordFilter, PricingRestTests, Ct, WebsiteOrderAcceptanceTests, Ct (+4 more)

### Community 28 - "Community 28"
Cohesion: 0.06
Nodes (23): EwaysVNext.Modules.Sourcing.Application.Domain.SourcePolicies, EwaysVNext.Modules.Sourcing.Application.Validation, EwaysVNext.Modules.Sourcing.Application.Domain.SourceListings, EwaysVNext.Modules.Sourcing.Application.Matching, EwaysVNext.Modules.Sourcing.Application.Domain.ImportJobs, EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords, EwaysVNext.Modules.Sourcing.Application.Pipeline, EwaysVNext.Modules.Sourcing.Application.Normalization (+15 more)

### Community 29 - "Community 29"
Cohesion: 0.05
Nodes (38): AdminCompositionQueries, AuditReadGateway, CustomerReadGateway, SourcingReadGateway, IAdminDashboardService, IAuditVerificationService, ICustomer360Service, IOrder360Service (+30 more)

### Community 30 - "Community 30"
Cohesion: 0.08
Nodes (14): AttributeBlueprintCreatedDomainEvent, UpdateAttributeBlueprintOptionInput, AttributeBlueprintDependencies, IAttributeBlueprintDependencyGateway, AttributeBlueprintIdValidator, AttributeBlueprintOptionIdsValidator, AttributeBlueprintOptionInputValidator, ChangeAttributeBlueprintDataTypeValidator (+6 more)

### Community 31 - "Community 31"
Cohesion: 0.07
Nodes (18): SellerBlock, TaxAssessment, PartnerOrderCreatedDomainEvent, CompositionTraceInput, IssuedDocumentRef, OrderLineInput, PartnerOrderHeader, VariantSnapshot (+10 more)

### Community 32 - "Community 32"
Cohesion: 0.07
Nodes (15): EwaysVNextDbContext, Categories, PaymentInstructions, EwaysVNextDbContextDesignTimeFactory, PaymentInstructionConfiguration, ICustomerResolutionPort, PaymentInstruction, ActiveVersion (+7 more)

### Community 33 - "Community 33"
Cohesion: 0.06
Nodes (21): SellableVariantReader, SellableVariant, IVariantLookupPort, VariantState, ISellableVariantReader, SellableVariantService, IFxSnapshotPort, IPriceQuotePort (+13 more)

### Community 34 - "Community 34"
Cohesion: 0.08
Nodes (10): PartnerOrderListQuery, AdminCompositionApiTests, Ct, PartnerOrderQueriesTests, Ct, SeededWorld, SalesAdminQueriesTests, Ct (+2 more)

### Community 35 - "Community 35"
Cohesion: 0.07
Nodes (24): AttributeBlueprintDataTypeFilter, Boolean, Date, Number, Option, Text, AttributeBlueprintFilter, AttributeBlueprintStatusFilter (+16 more)

### Community 36 - "Community 36"
Cohesion: 0.05
Nodes (32): CategoryConfiguration, DemoCatalogSeeder, CategoryRepository, HierarchyRow, Id, ParentId, Category, Aliases (+24 more)

### Community 37 - "Community 37"
Cohesion: 0.06
Nodes (22): Products, Product, BrandId, CreatedAt, DeactivatedByAdmin, Description, Identifiers, Media (+14 more)

### Community 38 - "Community 38"
Cohesion: 0.06
Nodes (26): VariantPrices, CurrencyOverrideConfiguration, VariantPriceConfiguration, FxResolution, CurrencyOverride, AmountMinor, Currency, PriceList (+18 more)

### Community 39 - "Community 39"
Cohesion: 0.09
Nodes (8): PartnerOrderAcceptanceTests, Ct, Counts, PartnerOrderFlowTests, Ct, VariantBand, PartnerOrderFormatTests, Ct

### Community 40 - "Community 40"
Cohesion: 0.06
Nodes (19): ApiKeyUsages, ApiKeyUsageConfiguration, ApiKeyUsage, At, CorrelationId, DurationMs, Id, KeyId (+11 more)

### Community 41 - "Community 41"
Cohesion: 0.06
Nodes (37): StagedRecords, StagedRecordConfiguration, StagedRecord, CategoryOverrideId, CreatedAt, Fingerprint, FirstJobId, ImageKeys (+29 more)

### Community 42 - "Community 42"
Cohesion: 0.06
Nodes (24): Partners, ApiKeyConfiguration, PartnerConfiguration, ScopeMapping, ApiKey, CreatedAt, ExpiresAt, PartnerId (+16 more)

### Community 43 - "Community 43"
Cohesion: 0.06
Nodes (22): CreateCategoryInput, ActingHttpContextAccessor, HttpContext, ApiFixture, App, Endpoints, Queries, CapturingService (+14 more)

### Community 44 - "Community 44"
Cohesion: 0.05
Nodes (34): EwaysVNext.Presentation.Endpoint.HttpHandlers, HttpLoggingHandler, HttpLoggingHandlerMessages, HttpLoggingOption, ModuleName, ServiceName, CrawlerOptions, AllowPrivateNetworks (+26 more)

### Community 45 - "Community 45"
Cohesion: 0.07
Nodes (16): EwaysVNext.Database.Sales.Configurations, EwaysVNext.Database.Customer.Configurations, EwaysVNext.Audit.Implementation, EwaysVNext.Database.Pricing.Configurations, AuthorizationService.Implementation.Seeding, EwaysVNext.Database.Audit.Configurations, EwaysVNext.Database.Catalog.Configurations, EwaysVNext.Database.Settings.Configurations (+8 more)

### Community 46 - "Community 46"
Cohesion: 0.12
Nodes (6): EwaysVNext.Database.Catalog.Views, EwaysVNext.Database.Migrations, AuthorizationService.Implementation.Persistence.Migrations, AuthorizationDbContextModelSnapshot, StorefrontViewSql, EwaysVNextDbContextModelSnapshot

### Community 47 - "Community 47"
Cohesion: 0.06
Nodes (25): JobLeaseLostException, JobId, ClaimedJob, IJobStore, JobBackoff, JobHandlerRegistry, KnownTypes, JobsMode (+17 more)

### Community 48 - "Community 48"
Cohesion: 0.07
Nodes (12): DemoCatalogData, Categories, Products, DemoCategory, DemoProduct, DemoVariant, ContractSnapshotTests, Ct (+4 more)

### Community 49 - "Community 49"
Cohesion: 0.06
Nodes (31): RandomSeedSource, DependencyInjection, BulkItem, BulkItemOutcome, BulkItemStatus, Created, Failed, Replayed (+23 more)

### Community 50 - "Community 50"
Cohesion: 0.09
Nodes (9): AddVariantCommand, BulkEditResult, BulkItemOutcome, CreateProductCommand, UpdateProductCommand, UploadMediaCommand, StorefrontScenario, CatalogAdminServiceTests (+1 more)

### Community 51 - "Community 51"
Cohesion: 0.07
Nodes (23): AdminCustomerListInput, CustomerAdminGraphQLQueries, ShopGraphQLSupport, UpdateProfileInput, ShopIdentity, ShopMeEndpoints, AddressSnapshot, WebsiteCustomerRef (+15 more)

### Community 52 - "Community 52"
Cohesion: 0.06
Nodes (23): EwaysVNext.Modules.Admin.Application.Services, EwaysVNext.Modules.Customer.Application.Customers.Domain, EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound, EwaysVNext.Modules.Admin, EwaysVNext.Modules.Admin.Application.Ports.Inbound, EwaysVNext.Modules.Admin.Application, EwaysVNext.Modules.Customer.Application.Customers.Services, EwaysVNext.Domain.ValueObjects (+15 more)

### Community 53 - "Community 53"
Cohesion: 0.07
Nodes (11): SalesSettingsGateway, SalesCommerceLimits, SettingsRepository, CommerceLimitsSnapshot, ISettingsPort, PaymentInstructionSnapshot, SellerSnapshot, ISettingsRepository (+3 more)

### Community 54 - "Community 54"
Cohesion: 0.08
Nodes (10): Error, Result, Error, IsFailure, IsSuccess, AttributeBlueprintService, PricingErrors, SalesErrors (+2 more)

### Community 55 - "Community 55"
Cohesion: 0.11
Nodes (14): CategoryAdminQueries, AddVariantInput, CreateProductInput, MergeDuplicateProductInput, ProductAdminMutations, RemoveProductIdentifierInput, RemoveProductMediaInput, SetProductActiveInput (+6 more)

### Community 56 - "Community 56"
Cohesion: 0.08
Nodes (9): QuestPdfDocumentRenderer, BuyerFieldRequirements, BuyerBlock, DocumentContent, DocumentLine, PaymentInstructionBlock, ResolvedBuyer, DocumentRulesTests (+1 more)

### Community 57 - "Community 57"
Cohesion: 0.08
Nodes (17): AddAttributeBlueprintOptionInput, AttributeBlueprintOptionInput, AttributeBlueprintOptionUpdateInput, CreateAttributeBlueprintInput, SetAttributeBlueprintAliasesInput, SetAttributeBlueprintScopeInput, SetAttributeBlueprintUnitInput, UpdateAttributeBlueprintInput (+9 more)

### Community 58 - "Community 58"
Cohesion: 0.08
Nodes (29): CatalogMatcherGateway, FuzzyCandidateRef, IssueSeverity, Error, Review, Warning, MatchKind, Ambiguous (+21 more)

### Community 59 - "Community 59"
Cohesion: 0.08
Nodes (8): CategoryCreatedDomainEvent, CategoryUpdatedDomainEvent, CategoryDomainTests, QueryFixture, AdminQueries, Db, Queries, QueryCounter

### Community 60 - "Community 60"
Cohesion: 0.10
Nodes (9): IDomainEventHandler, CategoryAttributeAssignedDomainEvent, CategoryAttributeBecameRequiredDomainEvent, CategoryAttributeFlagsChangedDomainEvent, CategoryAttributePriorityChangedDomainEvent, CategoryAttributeTitleConfigurationChangedDomainEvent, CategoryAttributeUnassignedDomainEvent, CategoryAttributeBecameRequiredHandler (+1 more)

### Community 61 - "Community 61"
Cohesion: 0.05
Nodes (40): Orders, OrderConfiguration, LinePricing, AtReference, WithinBand, Order, Buyer, Channel (+32 more)

### Community 62 - "Community 62"
Cohesion: 0.08
Nodes (18): CategoryAdminDto, ChildCount, Description, DisplayOrder, Id, Name, Parent, ParentId (+10 more)

### Community 63 - "Community 63"
Cohesion: 0.08
Nodes (17): DependencyInjection, SnowflakeIdJsonOptionsSetup, PersistenceDependencyInjection, DependencyInjection, DependencyInjection, DependencyInjection, DependencyInjection, DependencyInjection (+9 more)

### Community 64 - "Community 64"
Cohesion: 0.08
Nodes (17): AppGraphQLException, Code, HttpStatus, Retryable, ConcurrencyConflictGraphQLException, ConflictGraphQLException, ForbiddenGraphQLException, NotFoundGraphQLException (+9 more)

### Community 65 - "Community 65"
Cohesion: 0.10
Nodes (10): EndpointResult, ErrorBodyResult, PartnerAdminEndpoints, InvoiceSearchItemResponse, InvoiceSearchResponse, PartnerInvoiceEndpoints, SearchInvoicesRequest, SettingsAdminEndpoints (+2 more)

### Community 66 - "Community 66"
Cohesion: 0.06
Nodes (32): DocumentStatus, CreditNoted, Invoiced, None, ProFormaIssued, FulfillmentSource, SystemAutomatic, FulfillmentStatus (+24 more)

### Community 67 - "Community 67"
Cohesion: 0.05
Nodes (35): IApplicationDbContext, PermissionRules, PermissionRuleScopes, Permissions, Roles, Scopes, UserRoleScopes, PermissionRuleScope (+27 more)

### Community 68 - "Community 68"
Cohesion: 0.08
Nodes (16): CrawlCursor, WebsiteCrawlerAdapter, Link, FileImportAdapter, SearchCursor, WebSearchSourceAdapter, ImportJobSpec, UrlForm (+8 more)

### Community 69 - "Community 69"
Cohesion: 0.10
Nodes (14): PartnerPrincipal, DependencyInjection, IPartnerOrderQueries, CustomerKey, InvoiceSearchItem, MoneyMinor, PartnerInvoiceDto, PartnerOrderDto (+6 more)

### Community 70 - "Community 70"
Cohesion: 0.09
Nodes (13): EfAuditTrail, IIdGenerator, EcbFxRateProvider, Name, FxRateRepository, FxProviderBatch, FxQuote, IFxRateProvider (+5 more)

### Community 71 - "Community 71"
Cohesion: 0.05
Nodes (41): HotChocolate.Types.OffsetPagination, Microsoft.Extensions.Caching.Memory, Refit.Reflection, net10.0, HotChocolate.AspNetCore, HotChocolate.AspNetCore.Authorization, Microsoft.NET.Sdk, net10.0 (+33 more)

### Community 72 - "Community 72"
Cohesion: 0.07
Nodes (46): ArchitectureTests boundary rules, Keycloak realms and partner API key auth, Pure amount-driven composition engine, AGENTS.md project instructions, Migrations never on startup, EwaysVNext .NET 10 modular monolith, Modules Catalog Customer Partners Settings Pricing Sales Admin Sourcing, Money minor units bigint plus currency (+38 more)

### Community 73 - "Community 73"
Cohesion: 0.10
Nodes (18): SalesReadGateway, CompositionFailure, CompositionWindow, CurrencyAmount, DocumentView, Order360, OrderRow, OrderSheet (+10 more)

### Community 74 - "Community 74"
Cohesion: 0.09
Nodes (21): RecordProcessor, Clock, UtcNow, FakeListings, Added, FakePricePublisher, Calls, Ids (+13 more)

### Community 75 - "Community 75"
Cohesion: 0.08
Nodes (16): PartnerApiUsageMiddleware, DependencyInjection, PartnerRepository, ApiKeyUsageFlushService, ChannelApiKeyUsageRecorder, Reader, PartnerUsageOptions, BatchSize (+8 more)

### Community 76 - "Community 76"
Cohesion: 0.08
Nodes (20): BrandAdminRow, PaginatedQuery, Items, Page, PageCount, PageSize, TotalCount, BrandAdminGraphQLQueries (+12 more)

### Community 77 - "Community 77"
Cohesion: 0.05
Nodes (29): AggregateRoot, IAggregateRoot, ProductConfiguration, CommerceLimits, Currencies, SellerProfiles, CurrencyConfiguration, CommerceLimitsConfiguration (+21 more)

### Community 78 - "Community 78"
Cohesion: 0.08
Nodes (10): CategoryStatus, Active, Deleted, Inactive, SetCategoryStatusInput, UpdateCategoryInput, AttributeBlueprintPersistenceModelTests, CategoryPersistenceModelTests (+2 more)

### Community 79 - "Community 79"
Cohesion: 0.15
Nodes (10): CategoryAttributeExclusionConfiguration, CategoryAttributeExclusions, CategoryAttributeExclusion, CategoryId, CreatedAt, SourceAssignmentId, ICategoryAttributeExclusionRepository, CategoryAttributeExclusionService (+2 more)

### Community 80 - "Community 80"
Cohesion: 0.06
Nodes (33): net10.0, Microsoft.NET.Sdk, FluentValidation, FluentValidation.DependencyInjectionExtensions, net10.0, Microsoft.NET.Sdk, net10.0, HotChocolate.AspNetCore (+25 more)

### Community 81 - "Community 81"
Cohesion: 0.09
Nodes (16): EndpointConventionBuilderExtensions, AuthorizationPolicyNames, IEndpoint, AuthPolicies, AdminCompositionEndpoints, AuditVerificationEndpoints, AttributeAdminEndpoints, BrandAdminEndpoints (+8 more)

### Community 82 - "Community 82"
Cohesion: 0.07
Nodes (24): CompositionAttempts, CompositionAttemptConfiguration, CompositionAttemptRecorder, CompositionAttempt, At, CandidatePoolSize, Currency, DurationMs (+16 more)

### Community 83 - "Community 83"
Cohesion: 0.11
Nodes (5): RawListing, Fingerprint, SourcingSchemaTests, Ct, FingerprintTests

### Community 84 - "Community 84"
Cohesion: 0.08
Nodes (10): TransientFailure, ErrorResponse, EwaysAuthorizationResultHandler, GlobalExceptionHandlerMiddleware, RateLimitingOptions, AdminPermitPerMinute, AnonymousPermitPerMinute, RateLimitingSetup (+2 more)

### Community 85 - "Community 85"
Cohesion: 0.13
Nodes (15): AttributeBlueprintOptionType, AttributeBlueprintType, MeasurementUnitType, CategoryAdminParentType, CategoryAdminTreeNodeType, CategoryAdminType, DependencyInjection, ProductAdminDetailType (+7 more)

### Community 86 - "Community 86"
Cohesion: 0.10
Nodes (13): PartnerJsonResult, PartnerResults, Response, BulkErrorResponse, BulkOrdersResponse, BulkOutcomeResponse, CreateBulkOrdersRequest, CreatePartnerOrderRequest (+5 more)

### Community 87 - "Community 87"
Cohesion: 0.09
Nodes (14): BrandConfiguration, Brands, Brand, Aliases, CanonicalName, CreatedAt, DisplayName, IsActive (+6 more)

### Community 88 - "Community 88"
Cohesion: 0.09
Nodes (14): EwaysVNext.Queries.Implementation, Against, Guard, CursorPaginationQuery, Filter, NextCursor, PageSize, PreviousCursor (+6 more)

### Community 89 - "Community 89"
Cohesion: 0.08
Nodes (27): HotChocolate.AspNetCore, Microsoft.NET.Sdk, Microsoft.EntityFrameworkCore, Microsoft.NET.Sdk, HotChocolate.AspNetCore, HotChocolate.AspNetCore.Authorization, Microsoft.NET.Sdk, EFCore.NamingConventions (+19 more)

### Community 90 - "Community 90"
Cohesion: 0.13
Nodes (5): AdminJwtFactory, Acceptance13StaffPermissionTests, SuperAdminOnlyRoutes, ShopCartTests, Ct

### Community 91 - "Community 91"
Cohesion: 0.12
Nodes (37): E-commerce MVP design spec, Admin REST/GraphQL API, New ADRs 0017-0022, Partner API keys, Admin audit trail (hash-chain seals), Production build directory, Bulk orders, Amount-driven composition engine (+29 more)

### Community 92 - "Community 92"
Cohesion: 0.15
Nodes (4): CategoryAttributeAssignmentSnapshot, CategoryAttributeAssignmentMutationResolvers, ICategoryAttributeAssignmentService, CategoryAttributeAssignmentService

### Community 93 - "Community 93"
Cohesion: 0.12
Nodes (11): CsvImportReader, JsonImportReader, ImportFileFormat, Csv, Json, Xlsx, IImportFileReader, ImportFileContents (+3 more)

### Community 94 - "Community 94"
Cohesion: 0.08
Nodes (8): HexagonalBoundaryTests, AdminReadReferences, Modules, Root, SanctionedReferences, PartnerContractTests, Ct, PartnerOperations

### Community 95 - "Community 95"
Cohesion: 0.10
Nodes (15): Carts, CartConfiguration, Cart, Currency, CustomerId, Lines, TotalMinor, UpdatedAt (+7 more)

### Community 96 - "Community 96"
Cohesion: 0.12
Nodes (15): IDocumentPdfRenderer, Admin, DocumentAccess, DocumentForRendering, Partner, InvoiceService, InvoiceServiceTests, Ct (+7 more)

### Community 97 - "Community 97"
Cohesion: 0.07
Nodes (28): Microsoft.EntityFrameworkCore.Tools, Microsoft.Extensions.Configuration.EnvironmentVariables, Microsoft.Extensions.Configuration.UserSecrets, Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore, net10.0, EFCore.NamingConventions, Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.Design (+20 more)

### Community 98 - "Community 98"
Cohesion: 0.11
Nodes (15): ShopIdentity, ShopIdentityClaims, AddToCartInput, CartChangeGraph, CartGraph, CartGraphMapping, CartLineGraph, MoneyGraph (+7 more)

### Community 99 - "Community 99"
Cohesion: 0.07
Nodes (16): ValueObject, VariantConfiguration, CategorySlug, Value, Sku, Value, Variant, AttributeSignature (+8 more)

### Community 100 - "Community 100"
Cohesion: 0.13
Nodes (9): SeedSummary, IdentifierInput, ICatalogPublishingPort, PublishBrand, PublishedProduct, PublishedVariant, PublishMedia, PublishProductCommand (+1 more)

### Community 101 - "Community 101"
Cohesion: 0.07
Nodes (17): EwaysVNext.Host.Authentication, KeycloakClaimTypes, KeycloakOAuthDocumentTransformerMessages, KeycloakOptions, ExternalUrl, InternalUrl, Realms, KeycloakOptionsMessages (+9 more)

### Community 102 - "Community 102"
Cohesion: 0.08
Nodes (34): Phase 0 gap report, Phase 0 gate: 766 tests passed, Phase 4 gap report, k6 load script (resolved P8), No GraphQL for the partner channel (by design), Composition coverage simulator (resolved P8), Phase 4 gate: 1820 tests passed, Candidate pool cache is in-process (+26 more)

### Community 103 - "Community 103"
Cohesion: 0.14
Nodes (10): RightsDecision, RightsStatus, Allowed, ContentDenied, ImagesDenied, Unknown, DeterministicNormalizer, NormalizedProduct (+2 more)

### Community 104 - "Community 104"
Cohesion: 0.14
Nodes (12): CheckoutPayload, ShopCheckoutMutations, ShopOrderQueriesGraph, CallerClaims, PdfUrlResponse, ShopOrderEndpoints, ICheckoutService, IInvoiceService (+4 more)

### Community 105 - "Community 105"
Cohesion: 0.09
Nodes (19): SourceListings, SourceListingConfiguration, PriceObservation, AmountMinor, CollectedAt, Currency, TaxIncluded, VariantKey (+11 more)

### Community 106 - "Community 106"
Cohesion: 0.17
Nodes (7): AttributeBlueprintSnapshot, EffectiveCategoryAttributeSnapshot, CategoryAttributeExclusionQueryResolvers, CategoryHierarchyNode, ICategoryAttributeExclusionQueryService, CategoryAttributeExclusionQueryService, CategoryAttributeExclusionQueryServiceTests

### Community 107 - "Community 107"
Cohesion: 0.10
Nodes (13): InvalidPermissionRuleException, Permission, Code, CreatedAtUtc, Description, Id, Name, Rules (+5 more)

### Community 108 - "Community 108"
Cohesion: 0.07
Nodes (12): ConcatenatedStream, CanRead, CanSeek, CanWrite, Length, Position, ForwardOnlyStream, CanRead (+4 more)

### Community 109 - "Community 109"
Cohesion: 0.15
Nodes (4): ResolvedRateProjection, ResolvedRateRow, PricingPersistenceTests, Ct

### Community 110 - "Community 110"
Cohesion: 0.09
Nodes (8): FxRefreshRequest, ImportJobStepPayload, ImportStepScheduler, StagedRecordRepository, StagingResult, StagingService, ImportStepResult, IStagedRecordRepository

### Community 111 - "Community 111"
Cohesion: 0.09
Nodes (8): DynamicAuthorizationPolicyProvider, PermissionRequirement, PermissionCode, PermissionAuthorizationHandler, AuthorizationPolicyRegistration, PartnerScopeAuthorizationHandler, PartnerScopeRequirement, AdminPermissionMatrixTests

### Community 112 - "Community 112"
Cohesion: 0.18
Nodes (5): BrandAdminGraphQLMutations, IssuedApiKeyPayload, PartnerAdminGraphQLMutations, IPartnerAdminService, SettingsAdminGraphQLMutations

### Community 113 - "Community 113"
Cohesion: 0.10
Nodes (10): IAuditable, ModifiedAt, ModifiedBy, OccurredBy, OccurredOn, ISoftDeletable, IsDeleted, AuditInterceptor (+2 more)

### Community 114 - "Community 114"
Cohesion: 0.07
Nodes (17): DependencyInjection, RoleQueries, ScopeQueries, UserRoleScopeQueries, DependencyInjection, GraphQLBootstrapMarker, GraphQLHttpResponseFormatter, Mutation (+9 more)

### Community 115 - "Community 115"
Cohesion: 0.09
Nodes (17): AuditVerificationResult, IAuditVerifier, AuditEventRow, AuditHashing, Genesis, AuditSeal, EventId, Hash (+9 more)

### Community 116 - "Community 116"
Cohesion: 0.11
Nodes (11): IDataSeeder, Order, EndpointExtensions, HostCommands, Probe, DemoCatalogDataSeeder, Order, HostCommandsTests (+3 more)

### Community 117 - "Community 117"
Cohesion: 0.09
Nodes (9): EwaysVNext.Events.Implementation, EwaysVNext.Persistence.SoftDelete, EwaysVNext.Core.Implementation.FileTypeDetection, EwaysVNext.Events.Application, EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.EventHandlers, EwaysVNext.Core.Application.FileTypeDetection, EwaysVNext.Persistence.Audit, EwaysVNext.UnitTests.Seeding (+1 more)

### Community 118 - "Community 118"
Cohesion: 0.08
Nodes (23): FileType, Bmp, Docx, Gif, Gzip, Jpeg, OleCompoundFile, Pdf (+15 more)

### Community 119 - "Community 119"
Cohesion: 0.07
Nodes (23): CustomerConfiguration, Customers, Customer, BillingAddress, CompanyName, ContactChanges, CreatedAt, Email (+15 more)

### Community 120 - "Community 120"
Cohesion: 0.21
Nodes (3): ICategoryAttributeAssignmentRepository, CategoryAttributeAssignmentValidator, CategoryAttributeAssignmentServiceTests

### Community 121 - "Community 121"
Cohesion: 0.14
Nodes (29): MVP master implementation plan, AuthPolicies / AuthSchemes / EwaysClaimTypes, Cross-phase contracts, Error codes (spec 6.3), Global constraints (money, invoices, errors, routes), IApiKeyAuthenticator, IAuditTrail / AuditActor, ICustomerResolutionPort (+21 more)

### Community 122 - "Community 122"
Cohesion: 0.18
Nodes (4): ObjectStorageCatalogMediaStore, ICatalogMediaStore, CatalogMediaUploadTests, Ct

### Community 123 - "Community 123"
Cohesion: 0.10
Nodes (13): BraveOptions, ApiKey, BaseUrl, ResultsPerQuery, BraveResponse, BraveResult, BraveSearchProvider, IsEnabled (+5 more)

### Community 124 - "Community 124"
Cohesion: 0.11
Nodes (19): NetArchTest.Rules, QuestPDF, HotChocolate.AspNetCore, HotChocolate.AspNetCore.Authorization, HotChocolate.Data, Microsoft.NET.Sdk, Microsoft.NET.Sdk, Microsoft.NET.Sdk (+11 more)

### Community 125 - "Community 125"
Cohesion: 0.10
Nodes (12): DatabaseMigrator, ISellableVariantPort, PostgresFixture, AppConnectionString, MigratorConnectionString, RepositoryRoot, StorageEndpoint, SuperuserConnectionString (+4 more)

### Community 126 - "Community 126"
Cohesion: 0.10
Nodes (20): net10.0, Microsoft.NET.Sdk, net10.0, EFCore.NamingConventions, Microsoft.EntityFrameworkCore, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.NET.Sdk, net10.0 (+12 more)

### Community 127 - "Community 127"
Cohesion: 0.12
Nodes (11): SourcePolicies, SourcePolicyConfiguration, SourcePolicy, ContentUseAllowed, CrawlAllowed, CreatedAt, Domain, ImageUseAllowed (+3 more)

### Community 128 - "Community 128"
Cohesion: 0.11
Nodes (6): KeycloakAuthenticationExtensions, HostRewritingDocumentRetriever, KeycloakReadinessHealthCheck, RejectUnknownIssuerHandler, PartnerApiKeyAuthenticationHandler, DependencyInjection

### Community 129 - "Community 129"
Cohesion: 0.15
Nodes (9): AdminCompositionTraceDto, AdminOrderDetailDto, AdminPaymentDto, AdminRefundDto, DetailRow, DocumentRow, LineRow, DocumentDto (+1 more)

### Community 130 - "Community 130"
Cohesion: 0.18
Nodes (5): JobWorkerTests, TestHost, Factory, Worker, TestPayload

### Community 131 - "Community 131"
Cohesion: 0.08
Nodes (22): FsCheck, FsCheck.Xunit.v3, NSubstitute, PdfPig, net10.0, Microsoft.NET.Sdk, net10.0, Microsoft.EntityFrameworkCore (+14 more)

### Community 132 - "Community 132"
Cohesion: 0.17
Nodes (11): IJobQueue, RefreshStorefrontViewsJobHandler, Type, StorefrontRefreshTrigger, RefreshStorefrontViewsPayload, RecordingRefresher, Calls, RefreshStorefrontViewsJobHandlerTests (+3 more)

### Community 133 - "Community 133"
Cohesion: 0.14
Nodes (14): AdminWorld, Audited, Journal, Loads, Saves, Service, CountingUnitOfWork, FixedClock (+6 more)

### Community 134 - "Community 134"
Cohesion: 0.15
Nodes (6): BucketResult, Rate, CurrencyResult, SuccessRate, Program, Rng

### Community 135 - "Community 135"
Cohesion: 0.08
Nodes (26): Phase 2 gap report, Phase 2 gate: 1556 tests passed, ECB single point of failure for FX rates, "Published" never implies "priced", Demo seed could be pointed at production DB, Phase 6 gap report, NuGet NU1900 suppressed (Artifactory 403), PRD §10 coverage matrix not executable (deferred) (+18 more)

### Community 137 - "Community 137"
Cohesion: 0.12
Nodes (12): ICorrelationContext, CorrelationId, CorrelationContext, CorrelationId, CorrelationIdMiddleware, CorrelationIdMiddlewareTests, RecordingResponseFeature, Body (+4 more)

### Community 138 - "Community 138"
Cohesion: 0.08
Nodes (23): Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.AspNetCore.OpenApi, Microsoft.Extensions.Diagnostics.HealthChecks, OpenTelemetry.Exporter.OpenTelemetryProtocol, OpenTelemetry.Extensions.Hosting, OpenTelemetry.Instrumentation.AspNetCore, OpenTelemetry.Instrumentation.EntityFrameworkCore, OpenTelemetry.Instrumentation.Http (+15 more)

### Community 139 - "Community 139"
Cohesion: 0.10
Nodes (22): FileSamples, Bmp, Docx, EmptyZip, Executable, Gif87a, Gif89a, Gzip (+14 more)

### Community 140 - "Community 140"
Cohesion: 0.13
Nodes (8): ICandidatePoolGateway, ICompositionBudgetFactory, ExpiredBudgetFactory, FilteredCandidatePoolGateway, PoolFilter, SalesApiFactory, ServiceCollectionTestExtensions, StaleOncePoolGateway

### Community 141 - "Community 141"
Cohesion: 0.10
Nodes (12): Entity, DomainEvents, Id, RowVersion, IHasDomainEvents, DomainEvents, MeasurementUnitConfiguration, MeasurementUnits (+4 more)

### Community 142 - "Community 142"
Cohesion: 0.09
Nodes (19): net10.0, Microsoft.NET.Sdk, net10.0, Microsoft.EntityFrameworkCore, Microsoft.Extensions.Configuration.Abstractions, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Hosting.Abstractions, Microsoft.Extensions.Logging.Abstractions (+11 more)

### Community 143 - "Community 143"
Cohesion: 0.09
Nodes (21): JobRecord, Attempts, CompletedAt, CreatedAt, DedupeKey, Id, LastError, LockedBy (+13 more)

### Community 144 - "Community 144"
Cohesion: 0.14
Nodes (5): RobotsTxt, AllowAll, CrawlDelay, DenyAll, CrawlerSafetyTests

### Community 145 - "Community 145"
Cohesion: 0.12
Nodes (14): ExtractedProduct, StructuredDataExtractor, JsonLdBlock, OpenGraphMeta, OpenGraphMetaReversed, ImportRowMapper, ExtractionMethod, Csv (+6 more)

### Community 146 - "Community 146"
Cohesion: 0.17
Nodes (10): IJobHandler, Type, JobContext, UsagePruneJobHandler, Type, UsagePrunePayload, FxRefreshJobHandler, Type (+2 more)

### Community 147 - "Community 147"
Cohesion: 0.14
Nodes (7): RequirePermissionAttribute, IAuthorizationCheckService, CheckAuthorizationRequest, CheckAuthorizationResult, AuthorizationCheckService, AllowBackofficeRoles, AllowAll

### Community 148 - "Community 148"
Cohesion: 0.09
Nodes (15): ProductMediaConfiguration, UploadProductMediaInput, MediaUsageRights, Allowed, Denied, RightsUnknown, ProductMedia, Alt (+7 more)

### Community 149 - "Community 149"
Cohesion: 0.24
Nodes (5): CatalogMediaOptions, MaxBytes, BulkProductEdit, CatalogBulkAndMergeTests, Ct

### Community 150 - "Community 150"
Cohesion: 0.16
Nodes (7): DependencyInjection, IPricingAdminQueries, PricingAdminQueries, CurrencyAdminDto, CurrencyOverrideDto, FxRateDto, VariantPriceAdminDto

### Community 151 - "Community 151"
Cohesion: 0.16
Nodes (9): AdminOrderQueries, AdminOrderEndpoints, AdminChannelFilter, PartnerApi, Website, AdminOrderFilter, AdminOrderListItemDto, CursorPage (+1 more)

### Community 152 - "Community 152"
Cohesion: 0.15
Nodes (4): EwaysVNext.Host.OpenApi, KeycloakOAuthDocumentTransformer, KeycloakSecurityRequirementOperationTransformer, PartnerApiKeyOpenApiTransformer

### Community 153 - "Community 153"
Cohesion: 0.12
Nodes (15): net10.0, Microsoft.NET.Sdk, net10.0, Microsoft.EntityFrameworkCore, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.NET.Sdk, net10.0, Microsoft.NET.Sdk (+7 more)

### Community 154 - "Community 154"
Cohesion: 0.16
Nodes (4): Money, Amount, Currency, MoneyTests

### Community 155 - "Community 155"
Cohesion: 0.30
Nodes (3): PublishVariant, CatalogPublishingServiceTests, Ct

### Community 156 - "Community 156"
Cohesion: 0.20
Nodes (5): ApiKeyOptions, MaxRotationOverlap, RotationOverlap, CreatePartnerCommand, PartnerAdminServiceTests

### Community 157 - "Community 157"
Cohesion: 0.18
Nodes (6): SettingsAdminGraphQLQueries, ISettingsAdminQueries, SettingsAdminQueries, CommerceLimitsView, PaymentInstructionVersionView, SellerView

### Community 159 - "Community 159"
Cohesion: 0.17
Nodes (18): docker-compose.yml (production), api service, caddy service, Jobs__Mode (api Disabled, worker runs jobs), keycloak service, keycloak-db service, migrations service (--migrate), minio service (+10 more)

### Community 160 - "Community 160"
Cohesion: 0.10
Nodes (20): Backoffice realm export, backoffice-console client (public, PKCE), roles super_admin, staff, CONFIGURE_TOTP default required action, Phase 1 gap report, Phase 1 gate: 1067 tests passed, Acceptance 13 partly covered in P1, Rate limiter is in-process (ADR-0010) (+12 more)

### Community 161 - "Community 161"
Cohesion: 0.22
Nodes (4): AncestorProjection, CategoryQueries, CategoryProjection, CategoryQueryShapes

### Community 162 - "Community 162"
Cohesion: 0.14
Nodes (11): IClock, UtcNow, SystemClock, UtcNow, DependencyInjection, DependencyInjection, DependencyInjection, DependencyInjection (+3 more)

### Community 163 - "Community 163"
Cohesion: 0.11
Nodes (18): Money stored as amount_minor bigint plus currency char(3), Order aggregate owns payment, fulfillment, documents, refunds, Website orders use the same aggregate, Partner orders are never jobs, Closed scope set narrowed at key, Money as minor units plus ISO currency, Composition engine pure and deterministic, +-5% band, Partner amount-driven order composition endpoints (+10 more)

### Community 164 - "Community 164"
Cohesion: 0.10
Nodes (15): OrderLine, Attributes, BandEnforced, BrandName, CategoryPath, DeviationBps, LineNo, LineTotalMinor (+7 more)

### Community 165 - "Community 165"
Cohesion: 0.20
Nodes (5): CompositionOptions, IterationBudget, IsExhausted, CompositionPropertyTests, MandatedAmountTests

### Community 166 - "Community 166"
Cohesion: 0.23
Nodes (4): ApiKeyLookup, ApiKeyCredential, IApiKeyLookup, ApiKeyAuthenticatorTests

### Community 167 - "Community 167"
Cohesion: 0.13
Nodes (15): net10.0, HotChocolate.AspNetCore, HotChocolate.AspNetCore.Authorization, HotChocolate.Data, Microsoft.NET.Sdk, net10.0, Microsoft.NET.Sdk, net10.0 (+7 more)

### Community 168 - "Community 168"
Cohesion: 0.24
Nodes (4): CompositionEngine, Pick, Quantity, CompositionCandidate

### Community 169 - "Community 169"
Cohesion: 0.15
Nodes (9): PartnerScopes, All, DependencyInjection, ApiKeyAuthResult, IApiKeyAuthenticator, ApiKeyAuthenticator, BuyerFieldRules, Fields (+1 more)

### Community 170 - "Community 170"
Cohesion: 0.14
Nodes (9): CompletedOutcome, FailedOutcome, JobOutcome, Completed, RetryLaterOutcome, Registration, TestHandler, Outcome (+1 more)

### Community 171 - "Community 171"
Cohesion: 0.15
Nodes (9): ProductListQuery, ProductPageDto, StorefrontOptions, MediaBaseUrl, CrumbRow, CategoryId, Name, Slug (+1 more)

### Community 172 - "Community 172"
Cohesion: 0.16
Nodes (7): Address, City, CountryIso2, Line1, Line2, PostalCode, AddressTests

### Community 173 - "Community 173"
Cohesion: 0.17
Nodes (5): CompositionScoring, ComposedLine, LineTotalMinor, CompositionVerifier, CompositionVerifierTests

### Community 174 - "Community 174"
Cohesion: 0.23
Nodes (3): DraftLine, ExactHit, ExactHitTests

### Community 175 - "Community 175"
Cohesion: 0.14
Nodes (11): ImportJobStatus, Cancelled, Completed, Draft, Failed, Paused, Queued, Running (+3 more)

### Community 176 - "Community 176"
Cohesion: 0.33
Nodes (3): SellableVariantPortTests, Ct, World

### Community 177 - "Community 177"
Cohesion: 0.16
Nodes (9): CategoryProjectionGraphQLTests, CommandCaptureInterceptor, CommandText, FixedHttpContextAccessor, HttpContext, QueryReachedDatabaseException, SuppressConnectionOpeningInterceptor, QueryReachedDatabaseException (+1 more)

### Community 178 - "Community 178"
Cohesion: 0.25
Nodes (6): ChannelCurrencyTotalsDto, CompositionFailureDto, CompositionStatsDto, CurrencyAmountDto, PendingWebsitePaymentsDto, SalesDashboardDto

### Community 179 - "Community 179"
Cohesion: 0.12
Nodes (17): Npgsql, snake_case naming convention, Two DB roles: eways_migrator and eways_app, Exponential retry backoff, dedupe_key unique among queued rows, Enqueue after the save, never inside, Jobs:Mode InProcess/WorkerOnly/Disabled, Explicit leases, at-least-once delivery, idempotent handlers, Postgres-backed job queue with SKIP LOCKED (+9 more)

### Community 180 - "Community 180"
Cohesion: 0.34
Nodes (3): AuditSealTests, MutableClock, UtcNow

### Community 181 - "Community 181"
Cohesion: 0.21
Nodes (8): ProductPageSqlBuilder, Parameters, StorefrontSort, Newest, Popular, PriceAsc, PriceDesc, Relevance

### Community 182 - "Community 182"
Cohesion: 0.17
Nodes (8): MoneyDto, AddCartLineRequest, CartChangeResponse, CartLineResponse, CartResponse, SetCartCurrencyRequest, ShopMapping, UpdateCartLineRequest

### Community 184 - "Community 184"
Cohesion: 0.17
Nodes (3): AdminArchitectureTests, PartnerTenancyArchitectureTests, ProductShapeTests

### Community 185 - "Community 185"
Cohesion: 0.13
Nodes (9): DependencyInjection, MinioOptions, AccessKey, Endpoint, PublicEndpoint, Region, SecretKey, MinioOptionsValidator (+1 more)

### Community 186 - "Community 186"
Cohesion: 0.14
Nodes (9): AuditDiff, FlexibleLongConverter, SnowflakeIdJsonConverter, SourcingJson, Options, PartnerJson, Options, DocumentContentJson (+1 more)

### Community 187 - "Community 187"
Cohesion: 0.12
Nodes (15): CategoryAttributeAssignmentConfiguration, CategoryAttributeAssignments, CategoryAttributeAssignment, AttributeBlueprintId, CategoryId, CreatedAt, IsFilterable, IsInTitle (+7 more)

### Community 189 - "Community 189"
Cohesion: 0.15
Nodes (8): EwaysVNext.Domain.Common.Exceptions, BusinessRule, IBusinessRule, Message, BaseException, StatusCode, BusinessRuleException, EntityNotFoundException

### Community 191 - "Community 191"
Cohesion: 0.15
Nodes (11): IFileTypeDetector, HeaderLength, FileTypeDetector, HeaderLength, OfficeOpenXmlInspector, ContentTypesEntry, ExcelFolder, LocalFileHeaderSignature (+3 more)

### Community 193 - "Community 193"
Cohesion: 0.18
Nodes (4): ResultGraphQLExtensions, CustomerAdminGraphQLMutations, ICustomerAdminService, ResultGraphQLExtensionsTests

### Community 194 - "Community 194"
Cohesion: 0.14
Nodes (13): ContactChangeConfiguration, ContactChange, ChangedAt, CorrelationId, CustomerId, Field, NewValue, OldValue (+5 more)

### Community 195 - "Community 195"
Cohesion: 0.19
Nodes (9): StopwatchBudgetFactory, CompositionPlan, CompositionStats, ExpiredBudget, IsExhausted, ICompositionBudget, IsExhausted, StopwatchBudget (+1 more)

### Community 196 - "Community 196"
Cohesion: 0.21
Nodes (8): DocumentNumberAllocator, OrderNumberFormat, DocumentNumberKind, Commercial, CreditNote, Order, ProForma, FakeNumbers

### Community 198 - "Community 198"
Cohesion: 0.30
Nodes (4): IDocumentStore, BrokenDocumentStore, PartnerInvoiceTests, Ct

### Community 199 - "Community 199"
Cohesion: 0.20
Nodes (4): CategoryCrumbDto, MoneyDto, ProductDetailDto, VariantOfferDto

### Community 200 - "Community 200"
Cohesion: 0.35
Nodes (4): EcbFxRateProviderTests, Ct, StubHandler, ThrowingHandler

### Community 201 - "Community 201"
Cohesion: 0.39
Nodes (4): SnowflakeIdGenerator, FixedClock, UtcNow, SnowflakeIdGeneratorTests

### Community 202 - "Community 202"
Cohesion: 0.15
Nodes (11): SaveOutcome, ConcurrencyConflict, Saved, UniqueViolation, InMemoryCategoryUnitOfWork, LastToken, NextSaveError, NextSaveOutcome (+3 more)

### Community 203 - "Community 203"
Cohesion: 0.34
Nodes (3): ApiKeyHasher, ApiKeyPepper, ApiKeyHasherTests

### Community 204 - "Community 204"
Cohesion: 0.12
Nodes (12): DocumentType, Commercial, CreditNote, ProForma, DocumentWording, OrderDocument, Content, IssuedAt (+4 more)

### Community 206 - "Community 206"
Cohesion: 0.14
Nodes (13): AGENTS.md rules, ADR-0011 Broker-free bus-free architecture, InMemory dev fallback only, MinIO behind IObjectStorage port, Named schema per module, no fallback, ITransactionExecutor deleted, Repeatable idempotent SQL scripts, Testcontainers Postgres replaces LocalDB (+5 more)

### Community 207 - "Community 207"
Cohesion: 0.16
Nodes (13): ADR-0010 Kubernetes deployment strategy, Foreign record is 404 not 403, HMAC-SHA256 with server-side pepper, Partner key format pk_prefix_secret, Partner id comes only from the key, Per-partner in-process rate limiting, Rotation with overlap, revocation, suspension, Usage log off the request path (+5 more)

### Community 208 - "Community 208"
Cohesion: 0.14
Nodes (15): Phase 3 gap report, Facets ignore per-category attribute exclusions (deferred), No incremental refresh for materialized views (deferred), Phase 3 gate: 1622 tests passed, Refresh cost grows with catalogue, Search quality is untuned, View rows can outlive their FX rate, Partner order atomic via one Order aggregate save (+7 more)

### Community 209 - "Community 209"
Cohesion: 0.19
Nodes (8): CategoryAttributeAssignmentAdminRow, CategoryAttributePriorityGroup, Primary, Standard, VariantGenerating, ICategoryAttributeAssignmentQueryService, CategoryAttributeAssignmentQueryResolvers, CategoryAttributeAssignmentQueryService

### Community 210 - "Community 210"
Cohesion: 0.13
Nodes (14): runs-on, steps, jobs, build-test, name, on, pull_request, push (+6 more)

### Community 211 - "Community 211"
Cohesion: 0.16
Nodes (7): CategoryProductConfiguration, CategoryProducts, CategoryProduct, AssignedAt, AssignedByUserId, CategoryId, ProductId

### Community 213 - "Community 213"
Cohesion: 0.14
Nodes (13): AuditEvent, Action, ActorRole, ActorUserId, After, Before, CorrelationId, Id (+5 more)

### Community 214 - "Community 214"
Cohesion: 0.13
Nodes (7): AddIdentifierRequest, AddVariantRequest, BulkProductRequest, CreateProductRequest, SetActiveRequest, SetSecondaryCategoriesRequest, UpdateProductRequest

### Community 218 - "Community 218"
Cohesion: 0.21
Nodes (3): ApiKeyFormat, GeneratedApiKey, ApiKeyFormatTests

### Community 222 - "Community 222"
Cohesion: 0.19
Nodes (10): API, API_PORT, app_role_cannot_create_tables(), check(), cleanup(), compose(), COMPOSE_DIR, ENV_FILE (+2 more)

### Community 223 - "Community 223"
Cohesion: 0.25
Nodes (5): IMeasurementUnitQueryService, MeasurementUnitSnapshot, IMeasurementUnitRepository, MeasurementUnitQueryService, MeasurementUnitQueryServiceTests

### Community 224 - "Community 224"
Cohesion: 0.20
Nodes (4): EchoSnowflakePayload, SnowflakeIdGraphQLTests, SnowflakeSample, SnowflakeSampleInput

### Community 225 - "Community 225"
Cohesion: 0.21
Nodes (4): AdminAuditCoverageTests, ScopeFactory, ScopesCreated, ServiceProvider

### Community 226 - "Community 226"
Cohesion: 0.15
Nodes (9): Role, Code, CreatedAtUtc, Id, Name, Rank, UpdatedAtUtc, Roles (+1 more)

### Community 227 - "Community 227"
Cohesion: 0.22
Nodes (4): PartnerOrderEndpoints, ByExternalId, ById, PrincipalTests

### Community 229 - "Community 229"
Cohesion: 0.14
Nodes (14): ProductPageRow, BrandId, BrandName, CategoryId, CreatedAt, ImageKey, MaxPriceMinor, MinPriceMinor (+6 more)

### Community 230 - "Community 230"
Cohesion: 0.25
Nodes (6): AdminOrderMutations, CancelOrderInput, ConfirmOrderPaymentInput, RefundOrderInput, IOrderAdminService, ISalesAdminQueries

### Community 231 - "Community 231"
Cohesion: 0.23
Nodes (7): LinePriceSource, Base, Fx, Override, ShopDocumentRow, ShopLineRow, ShopOrderQueries

### Community 233 - "Community 233"
Cohesion: 0.15
Nodes (12): Microsoft.AspNetCore.Mvc.Testing, Microsoft.IdentityModel.Tokens, System.IdentityModel.Tokens.Jwt, Testcontainers.LocalStack, Testcontainers.PostgreSql, net10.0, AwesomeAssertions, AWSSDK.S3 (+4 more)

### Community 234 - "Community 234"
Cohesion: 0.17
Nodes (4): Email, Value, Phone, Value

### Community 236 - "Community 236"
Cohesion: 0.23
Nodes (12): Phase 7 gap report, Claude enrichment not built, Image staging into object storage not built, Keyword search (Brave) half of acceptance 15 not built, Microdata extraction not built, No GraphQL surface for imports, XLSX file reader not built, Phase 7 gate: 2186 tests passed (+4 more)

### Community 237 - "Community 237"
Cohesion: 0.27
Nodes (4): FacetGroupDto, FacetSetDto, FacetValueDto, PriceBucketDto

### Community 238 - "Community 238"
Cohesion: 0.18
Nodes (9): PermissionRule, Id, Permission, PermissionId, Role, RoleId, Scopes, PermissionRules (+1 more)

### Community 239 - "Community 239"
Cohesion: 0.17
Nodes (10): IUserProfileService, Email, IdentityUserId, PhoneNumber, UserName, UserProfileService, Email, IdentityUserId (+2 more)

### Community 240 - "Community 240"
Cohesion: 0.17
Nodes (6): BulkEditProductsInput, CatalogAdminEndpoints, BulkStatusChange, Activate, Deactivate, None

### Community 241 - "Community 241"
Cohesion: 0.17
Nodes (12): HomeProductRow, BrandId, BrandName, CategoryId, ImageKey, MaxPriceMinor, MinPriceMinor, ProductId (+4 more)

### Community 242 - "Community 242"
Cohesion: 0.18
Nodes (3): EwaysVNext.Presentation.Endpoint.Attributes, DisallowedEnumValuesAttribute, DisallowedEnumValuesAttributeMessages

### Community 243 - "Community 243"
Cohesion: 0.24
Nodes (8): ADR-0009 Supplier-Brand M2M, Per-module configuration files (ModuleAppSetting), Configuration precedence trap: re-add AddEnvironmentVariables, Payment gateway ACL for Iranian IPGs (superseded), Retained-provider dispatch, HexagonalBoundaryTests.ModuleNames guard, Remove Payment, Location, Notification modules; Customer shell, Out of scope: payment gateway, shipping, stock, webhooks

### Community 244 - "Community 244"
Cohesion: 0.25
Nodes (3): CategoryTileDto, HomeDto, ProductCardDto

### Community 246 - "Community 246"
Cohesion: 0.36
Nodes (3): CategoryAttributeExclusionMutationResolvers, CategoryAttributeExclusionSnapshot, ICategoryAttributeExclusionService

### Community 247 - "Community 247"
Cohesion: 0.18
Nodes (5): CreatePartnerRequest, IssueKeyRequest, RotateKeyRequest, SetLimitsRequest, SetScopesRequest

### Community 250 - "Community 250"
Cohesion: 0.27
Nodes (6): Admin API: catalog, pricing, orders, partners, settings, imports, audit, Import jobs and staged records (Sourcing), GraphQL per-resolver auth, depth 10, cost 20000, Authenticated shop customer endpoints: cart, checkout, orders, Checkout expectedTotal compared with live quote, Catalog import pipeline and duplicate controls

### Community 251 - "Community 251"
Cohesion: 0.20
Nodes (8): CursorPaginatedQuery, HasNextCursor, Items, NextCursor, PageCount, PageSize, PreviousCursor, TotalCount

### Community 253 - "Community 253"
Cohesion: 0.20
Nodes (10): ProductDetailRow, AncestorCategoryIds, BrandId, BrandName, Description, ImageKeys, ProductId, ShortDescription (+2 more)

### Community 254 - "Community 254"
Cohesion: 0.25
Nodes (7): Bruno local env vars (baseUrl localhost:5000, secrets), Bruno smoke env vars (baseUrl 127.0.0.1:18080), Minimum acceptance scenarios, k6 live load: 90 orders/s, p95 23.79 ms, Production compose stack smoke run, Live composition telemetry sales.composition_attempts, Verdict against PRD targets (99% success, p95 under 10 s)

### Community 255 - "Community 255"
Cohesion: 0.22
Nodes (3): CreatePermissionInput, PermissionRuleInput, UpdatePermissionInput

### Community 257 - "Community 257"
Cohesion: 0.25
Nodes (7): PaymentInstructionVersionConfiguration, PaymentInstructionVersion, CreatedAt, CreatedBy, PaymentInstructionId, Text, Version

### Community 260 - "Community 260"
Cohesion: 0.22
Nodes (7): Refund, AmountMinor, CreditNoteDocumentId, ExternalReference, Reason, RecordedAt, RecordedBy

### Community 262 - "Community 262"
Cohesion: 0.42
Nodes (3): Random64, SeedLikeCatalog, Currencies

### Community 263 - "Community 263"
Cohesion: 0.29
Nodes (7): Keycloak start-dev, mailpit (catches Keycloak mail), dev minio, dev postgres, Shop realm export, modular-monolith client (public, PKCE), verifyEmail + self-registration

### Community 265 - "Community 265"
Cohesion: 0.29
Nodes (7): ADR-0003 Query application separation, ADR-0008 Read-optimized CQS terminology, ADR-0012 QueryApplication is the public contract, ADR-0013 Narrow QueryApplication, Contracts project is the public API, Architecture test enforcement rules for read side, QueryApplication is the read side, queries DbContext directly

### Community 267 - "Community 267"
Cohesion: 0.25
Nodes (4): composed, CURRENCIES, options, refused

### Community 268 - "Community 268"
Cohesion: 0.25
Nodes (3): FileSignature, FileType, Refiner

### Community 269 - "Community 269"
Cohesion: 0.32
Nodes (3): DependencyInjection, JobHandlerRegistration, RecurringJobRegistration

### Community 270 - "Community 270"
Cohesion: 0.29
Nodes (6): RecurringJobRecord, Interval, NextRunAt, Type, RecurringJobs, RecurringJobRecordConfiguration

### Community 272 - "Community 272"
Cohesion: 0.25
Nodes (7): PaymentRecord, AmountMinor, Method, RecordedAt, Reference, Source, VerifiedBy

### Community 274 - "Community 274"
Cohesion: 0.38
Nodes (3): Add-ProjectReferenceSafe(), Invoke-Dotnet(), New-ClassLibrary()

### Community 275 - "Community 275"
Cohesion: 0.43
Nodes (3): AdminPermissionMatrix, Rows, MatrixRow

### Community 276 - "Community 276"
Cohesion: 0.29
Nodes (5): AuthorizationSeedData, SeedUserRoleScope, RoleCode, ScopeCode, UserId

### Community 277 - "Community 277"
Cohesion: 0.29
Nodes (6): net10.0, AwesomeAssertions, Microsoft.EntityFrameworkCore.InMemory, Microsoft.Testing.Extensions.CodeCoverage, xunit.v3, Microsoft.NET.Sdk

### Community 278 - "Community 278"
Cohesion: 0.43
Nodes (3): RequestSample, Sample, SnowflakeIdJsonTests

### Community 281 - "Community 281"
Cohesion: 0.33
Nodes (3): AttributeOptionRequest, CreateAttributeRequest, UpdateAttributeRequest

### Community 283 - "Community 283"
Cohesion: 0.40
Nodes (3): AttributeBlueprintUsageSnapshot, CanChangeDataType, CanDelete

### Community 285 - "Community 285"
Cohesion: 0.40
Nodes (4): Gridify, Gridify.EntityFramework, net10.0, Microsoft.NET.Sdk

### Community 286 - "Community 286"
Cohesion: 0.40
Nodes (4): JwtOptions, Audience, Issuer, SigningKey

### Community 287 - "Community 287"
Cohesion: 0.40
Nodes (5): FacetRow, FacetKey, FacetKind, FacetValue, ProductCount

### Community 288 - "Community 288"
Cohesion: 0.40
Nodes (5): HomeCategoryRow, CategoryId, Name, ProductCount, Slug

### Community 289 - "Community 289"
Cohesion: 0.40
Nodes (5): VariantOfferRow, AttributeValues, PriceMinor, Sku, VariantId

## Knowledge Gaps
- **1467 isolated node(s):** `Text`, `Number`, `Boolean`, `Date`, `Option` (+1462 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 2272 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **58 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `EwaysVNextDbContext` connect `Community 32` to `Community 130`, `Community 3`, `Community 4`, `Community 7`, `Community 8`, `Community 11`, `Community 13`, `Community 270`, `Community 143`, `Community 16`, `Community 141`, `Community 18`, `Community 14`, `Community 146`, `Community 19`, `Community 150`, `Community 21`, `Community 25`, `Community 26`, `Community 157`, `Community 33`, `Community 162`, `Community 35`, `Community 36`, `Community 37`, `Community 38`, `Community 161`, `Community 40`, `Community 41`, `Community 42`, `Community 171`, `Community 166`, `Community 44`, `Community 39`, `Community 175`, `Community 34`, `Community 177`, `Community 50`, `Community 51`, `Community 180`, `Community 53`, `Community 311`, `Community 58`, `Community 187`, `Community 188`, `Community 61`, `Community 62`, `Community 63`, `Community 59`, `Community 66`, `Community 196`, `Community 69`, `Community 70`, `Community 75`, `Community 76`, `Community 77`, `Community 78`, `Community 79`, `Community 82`, `Community 211`, `Community 212`, `Community 213`, `Community 87`, `Community 220`, `Community 95`, `Community 231`, `Community 105`, `Community 109`, `Community 110`, `Community 115`, `Community 116`, `Community 119`, `Community 125`, `Community 127`?**
  _High betweenness centrality (0.043) - this node is a cross-community bridge._
- **What connects `Text`, `Number`, `Boolean` to the rest of the system?**
  _1467 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.026289217454020915 - nodes in this community are weakly interconnected._
- **Why does `Customer` connect `Community 119` to `Community 32`, `Community 192`, `Community 194`, `Community 0`, `Community 8`, `Community 10`, `Community 172`, `Community 45`, `Community 14`, `Community 77`, `Community 48`, `Community 13`, `Community 19`, `Community 52`, `Community 51`, `Community 216`, `Community 217`?**
  _High betweenness centrality (0.031) - this node is a cross-community bridge._
- **Should `Community 1` be split into smaller, more focused modules?**
  _Cohesion score 0.019597989949748745 - nodes in this community are weakly interconnected._
- **Why does `StorefrontQueries` connect `Community 171` to `Community 32`, `Community 288`, `Community 289`, `Community 229`, `Community 199`, `Community 13`, `Community 237`, `Community 47`, `Community 241`, `Community 19`, `Community 244`, `Community 181`, `Community 54`, `Community 253`, `Community 62`, `Community 287`?**
  _High betweenness centrality (0.026) - this node is a cross-community bridge._
- **Should `Community 2` be split into smaller, more focused modules?**
  _Cohesion score 0.021267955941322775 - nodes in this community are weakly interconnected._