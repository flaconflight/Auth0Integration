using Auth0Integration.Functions.Models;

namespace Auth0Integration.Functions.Services;

public interface ICreditContextStore
{
    void Store(string otc, CreditContextEntry entry);
    CreditContextEntry? Retrieve(string otc);
    void Remove(string otc);
}
