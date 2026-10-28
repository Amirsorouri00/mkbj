using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Notification.Application.Ports.Outbound;
using Microsoft.Extensions.Options;
using EwaysVNext.Outcomes.Extensions;

namespace EwaysVNext.Modules.Notification.Adapter.Outbound.Sms.PayamSms;

internal sealed partial class PayamSmsProvider(HttpClient httpClient, IOptions<PayamSmsOptions> options) : ISmsProvider
{
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Enc = "http://schemas.xmlsoap.org/soap/encoding/";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly XNamespace Xsd = "http://www.w3.org/2001/XMLSchema";
    private static readonly XNamespace Tns = "urn:SMSAPIwsdl";

    private readonly PayamSmsOptions _options = options.Value;

    public string Name => "PayamSms";

    public async Task<Result<SmsSendResult>> SendAsync(string recipient, string body, CancellationToken ct = default)
    {
        var normalizedRecipient = NormalizeRecipient(recipient);
        if (normalizedRecipient.IsFailure)
            return Result.Failure<SmsSendResult>(normalizedRecipient.Error!);

        var operation = new XElement(Tns + "Send",
            StringParam("organization", _options.Organization),
            StringParam("username", _options.Username),
            StringParam("password", _options.Password),
            StringParam("srcNumber", _options.Sender),
            StringParam("body", body),
            StringArrayParam("destNo", "Destinations", [normalizedRecipient.Value!]));

        var result = await CallAsync("urn:SMSAPIwsdl#Send", operation, ct);
        if (result.IsFailure)
            return Result.Failure<SmsSendResult>(result.Error!);

        var providerMessageId = FirstResultItems(result.Value!)
            .FirstOrDefault()?.Elements().FirstOrDefault(e => e.Name.LocalName == "ID")?.Value;

        return providerMessageId is not null
            ? Result.Success(new SmsSendResult(providerMessageId))
            : Result.Failure<SmsSendResult>(new Error("PAYAMSMS_SOAP_NO_RESULT", PayamSmsProviderMessages.NoSendResult));
    }

    private static Result<string> NormalizeRecipient(string recipient)
    {
        var match = IranMobileNumber().Match(recipient.Trim());
        return match.Success
            ? Result.Success("98" + match.Groups[1].Value)
            : Result.Failure<string>(new Error(
                "PAYAMSMS_INVALID_RECIPIENT",
                MessageFormat.Format(PayamSmsProviderMessages.InvalidRecipientFormat, recipient)));
    }

    private static List<XElement> FirstResultItems(XElement responseRoot) =>
        responseRoot
            .Descendants().FirstOrDefault(e => e.Name.LocalName == "return")
            ?.Elements().Where(e => e.Name.LocalName == "item").ToList() ?? [];

    private async Task<Result<XElement>> CallAsync(string soapAction, XElement operation, CancellationToken ct)
    {
        try
        {
            var envelope = BuildEnvelope(operation);
            using var content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");
            using var request = new HttpRequestMessage(HttpMethod.Post, string.Empty) { Content = content };
            request.Headers.TryAddWithoutValidation("SOAPAction", soapAction);

            using var response = await httpClient.SendAsync(request, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return Result.Failure<XElement>(new Error("PAYAMSMS_SOAP_HTTP_ERROR", MessageFormat.Format(PayamSmsProviderMessages.HttpErrorFormat, (int)response.StatusCode, raw)));

            var document = XElement.Parse(raw);
            var fault = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "Fault");
            if (fault is not null)
            {
                var faultString = fault.Descendants().FirstOrDefault(e => e.Name.LocalName == "faultstring")?.Value
                    ?? PayamSmsProviderMessages.SoapFault;
                return Result.Failure<XElement>(new Error("PAYAMSMS_SOAP_FAULT", faultString));
            }

            return Result.Success(document);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<XElement>(new Error("PAYAMSMS_UNAVAILABLE", ex.Message));
        }
        catch (XmlException ex)
        {
            return Result.Failure<XElement>(new Error("PAYAMSMS_SOAP_INVALID_RESPONSE", ex.Message));
        }
    }

    private static XDocument BuildEnvelope(XElement operation)
    {
        operation.Add(new XAttribute(Soap + "encodingStyle", Enc.NamespaceName));

        var envelope = new XElement(Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xsd", Xsd.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "enc", Enc.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "ns1", Tns.NamespaceName),
            new XElement(Soap + "Header"),
            new XElement(Soap + "Body", operation));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), envelope);
    }

    private static XElement StringParam(string name, string value) =>
        new(name, new XAttribute(Xsi + "type", "xsd:string"), value);

    private static XElement StringArrayParam(string name, string wsdlTypeName, List<string> values) =>
        new(name,
            new XAttribute(Xsi + "type", $"ns1:{wsdlTypeName}"),
            new XAttribute(Enc + "arrayType", $"xsd:string[{values.Count}]"),
            values.Select(v => new XElement("item", new XAttribute(Xsi + "type", "xsd:string"), v)));

    [GeneratedRegex(@"^(?:\+?98|0)?(9\d{9})$")]
    private static partial Regex IranMobileNumber();
}
