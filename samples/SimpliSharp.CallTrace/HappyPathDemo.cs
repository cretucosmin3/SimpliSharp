using SimpliSharp.Utilities.Logging;
using System.Threading.Tasks;

namespace SimpliSharp.Demos
{
    public class HappyPathDemo
    { 
        [CallTrace]
        public async Task RunAsync()
        {
            for (int i = 0; i < 500; i++)
            {
                await FirstStep(1);
            }
            
            await SecondStep("test");
        }

        [CallTrace]
        private async Task FirstStep(int number)
        {
            await Task.Delay(2);
        }

        [CallTrace]
        private async Task SecondStep(string text)
        {
            await Task.Delay(50);
        }
    }
}
