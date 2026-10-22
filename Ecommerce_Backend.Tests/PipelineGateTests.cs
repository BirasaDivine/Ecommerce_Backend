using NUnit.Framework;

namespace Ecommerce_Backend.Tests
{
    [TestFixture]
    public class PipelineGateTests
    {
        [Test]
        public void DeliberatelyFailing_ProvesTheCiGateBlocksTheMerge()
        {
            Assert.Fail("Deliberate failure to demonstrate the pipeline gate.");
        }
    }
}
