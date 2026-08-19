using Xunit;

// Tắt parallel test execution trong toàn bộ assembly.
// Lý do: các integration test chia sẻ static state (TokenCache trong IntegrationTestAuth,
// IntegrationTestFixtures IDs, DB seed). Nếu chạy parallel, race condition giữa các
// test class dẫn đến token/userId mismatch và DB conflict.
//
// Trade-off: chậm hơn nhưng ổn định. Integration tests có giá trị nhất khi chạy
// deterministic.
[assembly: CollectionBehavior(DisableTestParallelization = true)]