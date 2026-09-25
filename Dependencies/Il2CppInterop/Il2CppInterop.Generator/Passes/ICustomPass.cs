using Il2CppInterop.Generator.Contexts;

namespace Il2CppInterop.Generator.Passes;

public interface ICustomPass
{
    void DoPass(RewriteGlobalContext context);
}
