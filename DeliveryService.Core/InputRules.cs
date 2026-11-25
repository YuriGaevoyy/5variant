using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace DeliveryService.Core;

public static class InputRules
{
    public static string Required(string? value, string field, int max)
    {
        var trimmed = Optional(value, field, max);
        if (trimmed.Length == 0)
            throw new RuleException($"Заполните поле «{field}».");
        return trimmed;
    }

    public static string Optional(string? value, string field, int max)
    {
        var trimmed = (value ?? "").Trim();
        if (trimmed.Length > max)
            throw new RuleException($"Поле «{field}» допускает до {max} символов.");
        return trimmed;
    }

    public static decimal ParseAmount(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
            return 0;
        int separator = text.IndexOfAny([',', '.']);
        if ((separator >= 0 && text.Length - separator - 1 is < 1 or > 2) ||
            !decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var amount))
            throw new RuleException("Введите сумму без разделителей тысяч, максимум с двумя знаками после запятой.");
        CheckAmount(amount);
        return amount;
    }

    public static void CheckAmount(decimal amount)
    {
        if (amount < 0 || amount > 999_999_999 || amount != decimal.Round(amount, 2))
            throw new RuleException("Сумма должна быть неотрицательной, до 999 999 999 ₽ и максимум с двумя знаками после запятой.");
    }

    public static decimal ParseWeight(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
            return 0;
        if (!decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var weight))
            throw new RuleException("Введите вес числом, например 2,5.");
        CheckWeight(weight);
        return weight;
    }

    public static void CheckWeight(decimal weight)
    {
        if (weight is < 0 or > 1000 || weight != decimal.Round(weight, 2))
            throw new RuleException("Вес указывайте от 0 до 1000 кг, максимум с двумя знаками после запятой.");
    }

    public static RecipientInput Recipient(RecipientInput input)
    {
        var name = Required(input.Name, "Получатель", 150);
        var phone = Required(input.Phone, "Телефон", 40);
        var address = Required(input.Address, "Адрес", 220);
        var email = Optional(input.Email, "Эл. почта", 150);
        if (email.Length != 0 && new EmailAddressAttribute().IsValid(email) is not true)
            throw new RuleException("Введите корректный адрес электронной почты.");
        return new RecipientInput(name, phone, address, email);
    }

    public static void CheckVersion(Shipment shipment, int? version)
    {
        if (version is null)
            throw new RuleException("Укажите версию отправления.");
        if (shipment.Version != version.Value)
            throw new ShipmentConflictException();
    }
}
