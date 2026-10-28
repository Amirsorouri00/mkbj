using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat;

internal static class MellatStatus
{
    private static readonly Dictionary<string, string> Messages = new(StringComparer.Ordinal)
    {
        ["11"] = "Invalid card number.", ["12"] = "Insufficient funds.", ["13"] = "Incorrect PIN.",
        ["14"] = "PIN attempts exceeded.", ["15"] = "Invalid card.", ["16"] = "Withdrawal attempts exceeded.",
        ["17"] = "The payer cancelled the transaction.", ["18"] = "The card has expired.",
        ["19"] = "The withdrawal amount exceeded the card limit.", ["21"] = "The acceptor is invalid.",
        ["20"] = "Required customer authentication parameters were not sent.",
        ["23"] = "A security error occurred.", ["24"] = "The merchant credentials are invalid.",
        ["25"] = "The amount is invalid.", ["26"] = "The institution cannot send the request.",
        ["27"] = "The device cannot perform the requested transaction.",
        ["28"] = "The device is temporarily unable to respond.",
        ["29"] = "The merchant cannot perform the transaction.",
        ["30"] = "The previous request is still processing.", ["31"] = "The response is invalid.",
        ["32"] = "The input format is invalid.", ["33"] = "The account is invalid.",
        ["34"] = "A system error occurred.", ["35"] = "The date is invalid.",
        ["36"] = "The transaction destination is signed off.", ["37"] = "The PSP is signed off.",
        ["38"] = "A key change is in progress.", ["39"] = "Shaparak is not responding.",
        ["41"] = "The request is duplicated.", ["42"] = "The sale transaction was not found.",
        ["43"] = "Verify was already successfully requested.", ["44"] = "The verify request was not found.",
        ["45"] = "The transaction has already been settled.", ["46"] = "The transaction was not settled.",
        ["47"] = "The settle transaction was not found.", ["48"] = "The transaction has already been reversed.",
        ["51"] = "The transaction is duplicated.",
        ["54"] = "The reference transaction does not exist.", ["55"] = "The transaction is invalid.",
        ["56"] = "Card owner authentication failed.", ["57"] = "Cardholder identity inquiry failed.",
        ["61"] = "An error occurred while depositing.",
        ["62"] = "The callback URL is outside the merchant's registered domain.",
        ["98"] = "The static password usage limit was reached.", ["111"] = "The card issuer is invalid.",
        ["112"] = "A card issuer switch error occurred.", ["113"] = "No response was received from the card issuer.",
        ["114"] = "The card holder is not authorized.", ["415"] = "The session has expired.",
        ["115"] = "The transaction is not permitted.", ["116"] = "The card issuer cannot respond.",
        ["117"] = "The card issuer is temporarily unable to respond.",
        ["211"] = "The device temporarily cannot perform the transaction.",
        ["412"] = "The bill ID is invalid.", ["413"] = "The payment ID is invalid.",
        ["414"] = "The bill issuing organization is invalid.",
        ["416"] = "The information could not be recorded.", ["417"] = "The payer ID is invalid.",
        ["418"] = "Customer information definition failed.", ["419"] = "The number of data entries exceeded the limit.",
        ["421"] = "The merchant server IP is invalid.", ["995"] = "Card ownership could not be verified.",
        ["997"] = "The destination system is inactive."
    };

    private static readonly HashSet<string> UncertainCodes =
        ["26", "28", "30", "31", "34", "36", "37", "38", "39", "112", "113", "116", "117", "211", "997"];

    public static bool IsUncertain(string code) => UncertainCodes.Contains(code);

    public static bool IsDocumented(string code) => Messages.ContainsKey(code);

    public static Error Refusal(string code) => new(
        "MELLAT_STATUS_" + code,
        Messages.TryGetValue(code, out var message) ? message : "The provider refused the request.",
        422);
}
