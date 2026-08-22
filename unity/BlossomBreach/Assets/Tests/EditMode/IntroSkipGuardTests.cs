using System;
using System.Reflection;
using NUnit.Framework;

namespace BlossomBreach.Tests
{
    public sealed class IntroSkipGuardTests
    {
        [Test]
        public void RequiresPointerToLeaveTargetBeforeAcceptingPress()
        {
            Type guardType = Type.GetType("BlossomBreach.IntroSkipGuard, Assembly-CSharp");
            Assert.That(guardType, Is.Not.Null, "IntroSkipGuard must exist in the runtime assembly.");

            object guard = Activator.CreateInstance(guardType, 1.5f);
            MethodInfo tryAccept = guardType.GetMethod("TryAccept", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(tryAccept, Is.Not.Null);

            Assert.That(Invoke(tryAccept, guard, 0f, false, false, true), Is.False);
            Assert.That(Invoke(tryAccept, guard, 2f, false, true, true), Is.False,
                "A stale or repeated trigger press must not skip while the pointer never left the target.");
            Assert.That(Invoke(tryAccept, guard, 2.1f, false, false, false), Is.False);
            Assert.That(Invoke(tryAccept, guard, 2.2f, false, true, true), Is.True,
                "A fresh shot after aiming away and back at the target must skip.");
        }

        private static bool Invoke(
            MethodInfo method,
            object target,
            float elapsed,
            bool triggerHeld,
            bool triggerPressed,
            bool pointerInsideTarget)
        {
            return (bool)method.Invoke(target, new object[]
            {
                elapsed,
                triggerHeld,
                triggerPressed,
                pointerInsideTarget
            });
        }
    }
}
