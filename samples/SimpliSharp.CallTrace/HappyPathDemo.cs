

using SimpliSharp.Utilities.Logging;
using System.Threading.Tasks;

namespace SimpliSharp.Demos
{
    [TraceData]
    public class Address
    {
        [TraceProperty]
        public string? Street { get; set; }

        public string? City { get; set; }
    }

    [TraceData]
    public class User
    {
        [TraceProperty]
        public int Id { get; set; }

        [TraceProperty]
        public string? Name { get; set; }

        [TraceProperty]
        public Address? Address { get; set; }
    }

    public class HappyPathDemo
    { 
        [CallTrace]
        [TraceData(Level.InputAndOutput)]
        public async Task RunAsync()
        {
            var user = new User { Id = 1, Name = "John Doe", Address = new Address { Street = "123 Main St", City = "Anytown" } };
            await UpdateUser(user, "some other data");
            await UpdateUserName(user, "Jane Doe");
            await PrintUser(user);

            for (int i = 0; i < 10; i++)
            {
                await Calculate(5, 10);
            }

            for (int i = 0; i < 5; i++)
            {
                await NoOp();
            }

            var concatenated = await Concatenate("Hello", " World");
            await ProcessData(15, concatenated, true);
        }

        [CallTrace]
        [TraceData(Level.Input)]
        private async Task UpdateUser([TraceParam] User user, string otherData)
        {
            await Task.Delay(10);
            if (user.Address != null)
            {
                user.Address.City = "Othertown";
            }
        }

        [CallTrace]
        [TraceData(Level.Input)]
        private async Task UpdateUserName(User user, [TraceParam] string newName)
        {
            await Task.Delay(10);
            user.Name = newName;
        }

        [CallTrace]
        [TraceData(Level.Input)]
        private async Task PrintUser([TraceParam] User user)
        {
            await Task.Delay(5);
        }

        [CallTrace]
        [TraceData(Level.InputAndOutput)]
        private async Task<int> Calculate([TraceParam] int a, [TraceParam] int b)
        {
            await Task.Delay(2);
            return a + b;
        }

        [CallTrace]
        [TraceData(Level.InputAndOutput)]
        private async Task<string> Concatenate([TraceParam] string a, [TraceParam] string b)
        {
            await Task.Delay(3);
            return a + b;
        }

        [CallTrace]
        [TraceData(Level.Input)]
        private async Task ProcessData([TraceParam] int number, [TraceParam] string text, [TraceParam] bool flag)
        {
            await Task.Delay(1);
        }

        [CallTrace]
        private async Task NoOp()
        {
            await Task.Delay(1);
        }
    }
}
