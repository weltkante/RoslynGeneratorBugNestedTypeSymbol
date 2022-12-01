namespace BugRepro
{
    public class IUnknown
    {
        public struct Reference
        {
        }
    }

    [Sandbox]
    public partial class IFoo : IUnknown
    {
        partial struct Reference
        {
            public partial void Example1(ReadOnlySpan<IUnknown.Reference> span); // works
            public partial void Example2(ReadOnlySpan<IFoo.Reference> span); // works
            public partial void Example3(ReadOnlySpan<IBar.Reference> span); // breaks
        }
    }

    [Sandbox]
    public partial class IBar : IUnknown
    {
        partial struct Reference
        {
        }
    }
}
