using SimpliSharp.Utilities.Logging;
using System.Net.Mail;

// --- Custom Exceptions for Clarity ---
public class OutOfStockException : Exception
{
    public OutOfStockException(int productId) : base($"Product '{productId}' is out of stock.") { }
}

public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message) : base(message) { }
}

static class Program
{
    [CallTrace]
    static void Main()
    {
        var processor = new OrderProcessor();
        
        try
        {
            // We'll process three orders in parallel to test concurrency.
            // - Order 101: Will succeed.
            // - Order 202: Will have a handled failure (notification fails).
            // - Order 303: Will have a breaking failure (out of stock).
            processor.ProcessOrdersAsync(new List<int> { 101, 202, 303 }).Wait();
        }
        catch (Exception breakingException)
        {
            Console.WriteLine("--- A critical error occurred. Generating trace log. ---");
            // GetTrace will capture the entire call tree for the request ID.
            string traceOutput = CallTracer.GetTrace(breakingException);
            
            File.WriteAllText("trace.log", traceOutput);
            Console.WriteLine(traceOutput);
            // In a real app, you might write this to a file or send to a logging service.
            // File.WriteAllText("trace.log", traceOutput);
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
        
        // Task.WhenAll runs all tasks concurrently. If any task throws an
        // unhandled exception, it will be wrapped in an AggregateException.
        await Task.WhenAll(processingTasks);
    }
    
    [CallTrace]
    private async Task ProcessSingleOrderAsync(int orderId)
    {
        Console.WriteLine($"Processing order {orderId}...");
        await _inventory.CheckStockAsync(orderId);
        await ChargeCustomer(orderId);

        try
        {
            // This call might fail, but we'll handle it gracefully.
            await _notification.SendConfirmationEmailAsync(orderId);
        }
        catch (SmtpException ex)
        {
            // This is a handled exception. The trace should show it with a
            // different icon (⭕) because it didn't stop the program flow.
            Console.WriteLine($"Warning: Failed to send email for order {orderId}. Details: {ex.Message}");
        }
    }
    
    [CallTrace]
    private async Task ChargeCustomer(int orderId)
    {
        // Simulate a delay for a payment gateway call.
        await Task.Delay(50);
        
        // This order is designed to fail at the payment step.
        if (orderId == 303)
        {
            // This is the breaking exception. The trace should mark this with ❌.
            throw new PaymentGatewayException("Credit card declined: Insufficient funds.");
        }
    }
}

public class InventoryService
{
    [CallTrace]
    public async Task CheckStockAsync(int orderId)
    {
        await Task.Delay(20); // Simulate a database call.
        
        // To make it more complex, let's pretend order 303's product ID is 999
        if (orderId == 303)
        {
            try
            {
                // This call will fail, but the exception will be caught and
                // wrapped in a more specific exception type.
                await CheckProductAvailability(999);
            }
            catch (Exception ex)
            {
                // Re-throwing as a more specific exception is a common pattern.
                throw new OutOfStockException(999);
            }
        }
    }
    
    [CallTrace]
    private async Task CheckProductAvailability(int productId)
    {
        await Task.Delay(10);
        if (productId == 999)
        {
            throw new InvalidOperationException("SKU lookup failed in warehouse DB.");
        }
    }
}

public class NotificationService
{
    [CallTrace]
    public async Task SendConfirmationEmailAsync(int orderId)
    {
        await Task.Delay(30); // Simulate connecting to an email server.
        
        // This order is designed to have a handled notification failure.
        if (orderId == 202)
        {
            throw new SmtpException("SMTP server connection timeout.");
        }
    }
}