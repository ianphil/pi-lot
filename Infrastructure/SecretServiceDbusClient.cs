using System.Text;
using Tmds.DBus.Protocol;

namespace LlmSvc.Infrastructure;

internal sealed class SecretServiceDbusClient : ISecretServiceClient
{
    public IReadOnlyList<SecretServiceItem> SearchItems(string serviceName)
    {
        using var connection = CreateConnection();
        connection.ConnectAsync().GetAwaiter().GetResult();

        var (unlockedItems, lockedItems) = SearchItemPaths(connection, serviceName);
        var items = new List<SecretServiceItem>(unlockedItems.Length + lockedItems.Length);

        foreach (var itemPath in unlockedItems.OrderBy(static path => path.ToString(), StringComparer.Ordinal))
        {
            items.Add(ReadItem(connection, itemPath, isLocked: false));
        }

        foreach (var itemPath in lockedItems.OrderBy(static path => path.ToString(), StringComparer.Ordinal))
        {
            items.Add(ReadItem(connection, itemPath, isLocked: true));
        }

        return items;
    }

    public string? GetSecret(string itemPath)
    {
        using var connection = CreateConnection();
        connection.ConnectAsync().GetAwaiter().GetResult();

        var sessionPath = OpenPlainSession(connection);
        return ReadSecret(connection, itemPath, sessionPath);
    }

    private static DBusConnection CreateConnection()
    {
        var sessionAddress = DBusAddress.Session;
        if (sessionAddress is null)
        {
            throw new IOException("No D-Bus session address is available.");
        }

        return new DBusConnection(sessionAddress);
    }

    private static (ObjectPath[] UnlockedItems, ObjectPath[] LockedItems) SearchItemPaths(DBusConnection connection, string serviceName)
    {
        return connection.CallMethodAsync(CreateMessage(), static (message, _) =>
        {
            var reader = message.GetBodyReader();
            return (reader.ReadArrayOfObjectPath(), reader.ReadArrayOfObjectPath());
        }).GetAwaiter().GetResult();

        MessageBuffer CreateMessage()
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(
                destination: CopilotCredentialConstants.SecretServiceBusName,
                path: CopilotCredentialConstants.SecretServiceObjectPath,
                @interface: CopilotCredentialConstants.SecretServiceInterface,
                member: "SearchItems",
                signature: "a{ss}");

            var attributesStart = writer.WriteDictionaryStart();
            writer.WriteDictionaryEntryStart();
            writer.WriteString("service");
            writer.WriteString(serviceName);
            writer.WriteDictionaryEnd(attributesStart);

            return writer.CreateMessage();
        }
    }

    private static SecretServiceItem ReadItem(DBusConnection connection, ObjectPath itemPath, bool isLocked)
    {
        var properties = connection.CallMethodAsync(CreateMessage(), static (message, _) =>
        {
            return message.GetBodyReader().ReadDictionaryOfStringToVariantValue();
        }).GetAwaiter().GetResult();

        var label = properties.TryGetValue("Label", out var labelValue) ? labelValue.GetString() : null;
        var locked = properties.TryGetValue("Locked", out var lockedValue) ? lockedValue.GetBool() : isLocked;
        string? account = null;

        if (properties.TryGetValue("Attributes", out var attributesValue))
        {
            var attributes = attributesValue.GetDictionary<string, string>();
            attributes.TryGetValue("account", out account);
        }

        return new SecretServiceItem(itemPath.ToString(), label, account, locked);

        MessageBuffer CreateMessage()
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(
                destination: CopilotCredentialConstants.SecretServiceBusName,
                path: itemPath,
                @interface: CopilotCredentialConstants.PropertiesInterface,
                member: "GetAll",
                signature: "s");
            writer.WriteString(CopilotCredentialConstants.SecretItemInterface);
            return writer.CreateMessage();
        }
    }

    private static string OpenPlainSession(DBusConnection connection)
    {
        return connection.CallMethodAsync(CreateMessage(), static (message, _) =>
        {
            var reader = message.GetBodyReader();
            _ = reader.ReadVariantValue();
            return reader.ReadObjectPathAsString();
        }).GetAwaiter().GetResult();

        MessageBuffer CreateMessage()
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(
                destination: CopilotCredentialConstants.SecretServiceBusName,
                path: CopilotCredentialConstants.SecretServiceObjectPath,
                @interface: CopilotCredentialConstants.SecretServiceInterface,
                member: "OpenSession",
                signature: "sv");
            writer.WriteString("plain");
            writer.WriteVariant(string.Empty);
            return writer.CreateMessage();
        }
    }

    private static string? ReadSecret(DBusConnection connection, string itemPath, string sessionPath)
    {
        return connection.CallMethodAsync(CreateMessage(), static (message, _) =>
        {
            var reader = message.GetBodyReader();
            reader.AlignStruct();
            _ = reader.ReadObjectPathAsString();
            _ = reader.ReadArrayOfByte();
            var secretBytes = reader.ReadArrayOfByte();
            _ = reader.ReadString();

            return secretBytes.Length == 0 ? null : Encoding.UTF8.GetString(secretBytes);
        }).GetAwaiter().GetResult();

        MessageBuffer CreateMessage()
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(
                destination: CopilotCredentialConstants.SecretServiceBusName,
                path: itemPath,
                @interface: CopilotCredentialConstants.SecretItemInterface,
                member: "GetSecret",
                signature: "o");
            writer.WriteObjectPath(sessionPath);
            return writer.CreateMessage();
        }
    }
}
