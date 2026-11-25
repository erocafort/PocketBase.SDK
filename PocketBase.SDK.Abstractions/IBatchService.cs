namespace PocketBase.SDK.Abstractions;

/// <summary>
/// Transactional batch of record requests (port of the SDKs <c>BatchService</c>), created with
/// <see cref="IPocketBaseClient.CreateBatch"/>. Requires batch requests to be enabled in the PocketBase settings.
/// <code>
/// var batch = pb.CreateBatch();
/// batch.Collection("example1").Create(new { title = "a" });
/// batch.Collection("example2").Update("RECORD_ID", new { title = "b" });
/// batch.Collection("example3").Delete("RECORD_ID");
/// batch.Collection("example4").Upsert(new { id = "RECORD_ID", title = "c" });
/// var results = await batch.SendAsync();
/// </code>
/// </summary>
public interface IBatchService
{
    /// <summary>Starts constructing a batch request entry for the specified collection.</summary>
    ISubBatchService Collection(string collectionIdOrName);

    /// <summary>Sends the batch requests.</summary>
    /// <exception cref="PocketBaseException">On API/network errors.</exception>
    Task<IReadOnlyList<BatchResult>> SendAsync(CommonOptions? options = null, CancellationToken cancellationToken = default);
}

public interface ISubBatchService
{
    /// <summary>Registers a record upsert request (update if the body has an existing record <c>id</c>, otherwise create).</summary>
    void Upsert(object? body = null, RecordOptions? options = null);

    /// <summary>Registers a record create request.</summary>
    void Create(object? body = null, RecordOptions? options = null);

    /// <summary>Registers a record update request.</summary>
    void Update(string id, object? body = null, RecordOptions? options = null);

    /// <summary>Registers a record delete request.</summary>
    void Delete(string id, CommonOptions? options = null);
}
