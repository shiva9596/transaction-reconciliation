namespace TransactionReconciliation.Console.Services.Interfaces;

public interface ICardDataProtector
{
    string ComputeHash(string cardNumber);
    string GetLast4(string cardNumber);
}