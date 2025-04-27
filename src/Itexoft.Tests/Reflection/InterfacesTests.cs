// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Reflection;

namespace Itexoft.Tests.Reflection;

public sealed class InterfacesTests
{
    [Test]
    public void Wrap_UsesHandlerForBaseMembers()
    {
        var input = new Input();
        var handler = new Handler();
        var wrapped = Interfaces.Overlay<IChild, IRoot>(input, handler);

        wrapped.BaseValue = 123;
        Assert.That(handler.BaseSetCount, Is.EqualTo(1));
        Assert.That(input.BaseSetCount, Is.EqualTo(0));

        handler.BaseValue = 200;
        var baseValue = wrapped.BaseValue;
        Assert.That(baseValue, Is.EqualTo(200));
        Assert.That(handler.BaseGetCount, Is.EqualTo(1));
        Assert.That(input.BaseGetCount, Is.EqualTo(0));

        var echo = wrapped.Echo("x");
        Assert.That(echo, Is.EqualTo("handler:x"));
        Assert.That(handler.EchoCount, Is.EqualTo(1));
        Assert.That(input.EchoCount, Is.EqualTo(0));

        var value = 5;
        wrapped.Mutate(ref value, out var doubled);
        Assert.That(value, Is.EqualTo(15));
        Assert.That(doubled, Is.EqualTo(45));
        Assert.That(handler.MutateCount, Is.EqualTo(1));
        Assert.That(input.MutateCount, Is.EqualTo(0));

        var inValue = 4;
        var addIn = wrapped.AddIn(inValue);
        Assert.That(addIn, Is.EqualTo(14));
        Assert.That(handler.AddInCount, Is.EqualTo(1));
        Assert.That(input.AddInCount, Is.EqualTo(0));

        handler.RefValue = 321;
        input.RefValue = 123;
        ref readonly var refValue = ref wrapped.GetRef();
        Assert.That(refValue, Is.EqualTo(321));
        Assert.That(handler.GetRefCount, Is.EqualTo(1));
        Assert.That(input.GetRefCount, Is.EqualTo(0));
    }

    [Test]
    public void Wrap_UsesInputForDerivedMembers()
    {
        var input = new Input();
        var handler = new Handler();
        var wrapped = Interfaces.Overlay<IChild, IRoot>(input, handler);

        wrapped.ChildValue = 7;
        Assert.That(input.ChildSetCount, Is.EqualTo(1));
        Assert.That(handler.BaseSetCount, Is.EqualTo(0));

        input.ChildValue = 9;
        var childValue = wrapped.ChildValue;
        Assert.That(childValue, Is.EqualTo(9));
        Assert.That(input.ChildGetCount, Is.EqualTo(1));

        var sum = wrapped.Sum(2, 3);
        Assert.That(sum, Is.EqualTo(105));
        Assert.That(input.SumCount, Is.EqualTo(1));

        var mirrored = wrapped.Mirror("abc");
        Assert.That(mirrored, Is.EqualTo("abc"));
        Assert.That(input.MirrorCount, Is.EqualTo(1));
    }

    [Test]
    public void Wrap_ImplementsAllInterfaces()
    {
        var wrapped = Interfaces.Overlay<IChild, IRoot>(new Input(), new Handler());
        Assert.That(wrapped, Is.InstanceOf<IChild>());
        Assert.That(wrapped, Is.InstanceOf<IRoot>());
    }

    [Test]
    public void Wrap_AllowsHandlerClassType()
    {
        var input = new Input();
        var handler = new Handler();
        var wrapped = Interfaces.Overlay<IChild, Handler>(input, handler);

        var value = 2;
        wrapped.Mutate(ref value, out var doubled);
        Assert.That(value, Is.EqualTo(12));
        Assert.That(doubled, Is.EqualTo(36));
        Assert.That(handler.MutateCount, Is.EqualTo(1));
        Assert.That(input.MutateCount, Is.EqualTo(0));
    }

    public interface IRoot
    {
        int BaseValue { get; set; }
        string Echo(string value);
        void Mutate(ref int value, out int doubled);
        int AddIn(in int value);
        ref readonly int GetRef();
    }

    public interface IChild : IRoot
    {
        int ChildValue { get; set; }
        int Sum(int a, int b);
        T Mirror<T>(T value);
    }

    private sealed class Input : IChild
    {
        private int baseValue;
        private int childValue;
        private int refValue;

        public int BaseGetCount { get; private set; }
        public int BaseSetCount { get; private set; }
        public int EchoCount { get; private set; }
        public int MutateCount { get; private set; }
        public int AddInCount { get; private set; }
        public int GetRefCount { get; private set; }
        public int ChildGetCount { get; private set; }
        public int ChildSetCount { get; private set; }
        public int SumCount { get; private set; }
        public int MirrorCount { get; private set; }

        public int RefValue
        {
            get => this.refValue;
            set => this.refValue = value;
        }

        public int BaseValue
        {
            get
            {
                this.BaseGetCount++;

                return this.baseValue;
            }
            set
            {
                this.BaseSetCount++;
                this.baseValue = value;
            }
        }

        public int ChildValue
        {
            get
            {
                this.ChildGetCount++;

                return this.childValue;
            }
            set
            {
                this.ChildSetCount++;
                this.childValue = value;
            }
        }

        public string Echo(string value)
        {
            this.EchoCount++;

            return $"input:{value}";
        }

        public void Mutate(ref int value, out int doubled)
        {
            this.MutateCount++;
            value += 1;
            doubled = value * 2;
        }

        public int AddIn(in int value)
        {
            this.AddInCount++;

            return value + 1;
        }

        public ref readonly int GetRef()
        {
            this.GetRefCount++;

            return ref this.refValue;
        }

        public int Sum(int a, int b)
        {
            this.SumCount++;

            return a + b + 100;
        }

        public T Mirror<T>(T value)
        {
            this.MirrorCount++;

            return value;
        }
    }

    private sealed class Handler : IRoot
    {
        private int baseValue;
        private int refValue;

        public int BaseGetCount { get; private set; }
        public int BaseSetCount { get; private set; }
        public int EchoCount { get; private set; }
        public int MutateCount { get; private set; }
        public int AddInCount { get; private set; }
        public int GetRefCount { get; private set; }

        public int RefValue
        {
            get => this.refValue;
            set => this.refValue = value;
        }

        public int BaseValue
        {
            get
            {
                this.BaseGetCount++;

                return this.baseValue;
            }
            set
            {
                this.BaseSetCount++;
                this.baseValue = value;
            }
        }

        public string Echo(string value)
        {
            this.EchoCount++;

            return $"handler:{value}";
        }

        public void Mutate(ref int value, out int doubled)
        {
            this.MutateCount++;
            value += 10;
            doubled = value * 3;
        }

        public int AddIn(in int value)
        {
            this.AddInCount++;

            return value + 10;
        }

        public ref readonly int GetRef()
        {
            this.GetRefCount++;

            return ref this.refValue;
        }
    }
}
