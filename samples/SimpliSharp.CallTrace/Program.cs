using SimpliSharp.Utilities.Logging;
using System.Diagnostics;
using System.Net.Mail;

public class OutOfStockException : Exception
{
    public OutOfStockException(string message) : base(message) { }
}

public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message) : base(message) { }
}

static class Program
{
    [CallTrace]
    static async Task Main()
    {
        Stopwatch timer = Stopwatch.StartNew();

        var processor = new OrderProcessor();
        try
        {
            // We'll process three orders in parallel to test concurrency.
            // - Order 101: Will have a repeating call sequence where one fails.
            // - Order 901: Will succeed.
            // - Order 202: Will have a handled notification failure.
            // - Order 902: Will succeed.
            // - Order 303: Will have a breaking failure (payment fails).
            await processor.ProcessOrdersAsync(new List<int> { 101, 901, 202, 902, 303 });
        }
        catch (Exception breakingException)
        {
            timer.Stop();
            Console.WriteLine($"Execution took {timer.ElapsedMilliseconds} ms");
            Console.WriteLine("--- A critical error occurred. Generating trace log. ---");

            var getTraceTimer = Stopwatch.StartNew();
            string traceOutput = CallTracer.GetTrace(breakingException);
            getTraceTimer.Stop();

            await System.IO.File.WriteAllTextAsync("trace.log", traceOutput);
            Console.WriteLine(traceOutput);
            Console.WriteLine($"--- GetTrace() took {getTraceTimer.Elapsed.TotalMilliseconds:F2}ms to execute. ---");
        }
    }
}

public class OrderProcessor
{
    private readonly InventoryService _inventory = new();
    private readonly NotificationService _notification = new();
    
    [CallTrace]
    public async Task ProcessOrdersAsync(List<int> orderIds)
    {
        var processingTasks = orderIds.Select(ProcessSingleOrderAsync).ToList();
        await Task.WhenAll(processingTasks);
    }
    
    [CallTrace]
    private async Task ProcessSingleOrderAsync(int orderId)
    {
        Console.WriteLine($"Processing order {orderId}...");
        Exception? capturedException = null;

        if (orderId == 101)
        {
            // This block demonstrates a repeating call where one fails.
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    await _inventory.CheckStockAsync(orderId, i + 1);
                }
                catch (Exception ex)
                {
                    capturedException = ex;
                }
            }
        }
        else
        {
            await _inventory.CheckStockAsync(orderId, 1);
        }

        await ChargeCustomer(orderId);

        try
        {
            await _notification.SendConfirmationEmailAsync(orderId);
        }
        catch (SmtpException ex)
        {
            Console.WriteLine($"Warning: Failed to send email for order {orderId}. Details: {ex.Message}");
        }

        if (capturedException != null)
        {
            throw capturedException;
        }
    }
    
    [CallTrace]
    private async Task ChargeCustomer(int orderId)
    {
        await Task.Delay(20);
        if (orderId == 303)
        {
            throw new PaymentGatewayException("Credit card declined: Insufficient funds.");
        }
    }
}

public class InventoryService
{
    [CallTrace]
    public async Task CheckStockAsync(int orderId, int attemptNumber)
    {
        await Task.Delay(10);
        
        if (orderId == 101 && attemptNumber == 5) // 5th attempt fails
        {
            throw new OutOfStockException($"Failed to verify stock for order {orderId} on attempt {attemptNumber}.");
        }

        if (orderId == 303)
        {
            // Simulate a more complex failure for a different order
            await CheckProductAvailability(999);
        }
    }
    
    [CallTrace]
    private async Task CheckProductAvailability(int productId)
    {
        await Task.Delay(10);
        throw new InvalidOperationException("SKU lookup failed in warehouse DB.");
    }
}

public class NotificationService
{
    [CallTrace]
    public async Task SendConfirmationEmailAsync(int orderId)
    {
        await Task.Delay(15);
        if (orderId == 202)
        {
            throw new SmtpException("SMTP server connection timeout.");
        }
    }
}