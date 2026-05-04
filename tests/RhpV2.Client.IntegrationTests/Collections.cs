using RhpV2.TestSupport;
using Xunit;

namespace RhpV2.Client.IntegrationTests;

// xunit picks up [CollectionDefinition] from the test assembly only,
// so each project that consumes a TestSupport fixture re-declares its
// collection here. The fixture types themselves live in TestSupport
// and are shared.

[CollectionDefinition(nameof(XRouterCollection))]
public sealed class XRouterCollection : ICollectionFixture<XRouterFixture> { }

[CollectionDefinition(nameof(XRouterPairCollection))]
public sealed class XRouterPairCollection : ICollectionFixture<XRouterPairFixture> { }
