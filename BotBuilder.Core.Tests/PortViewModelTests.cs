using System.ComponentModel;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class PortViewModelTests
{
    [Fact]
    public void MoveTo_UpdatesAnchor_AndRaisesChangeNotification()
    {
        var port = new PortViewModel("branch3", PortDirection.Out, PortEdge.Right, default);
        var raised = false;
        ((INotifyPropertyChanged)port).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PortViewModel.AnchorOffset)) { raised = true; }
        };

        port.MoveTo(new CanvasPoint(160, 70));

        Assert.Equal(new CanvasPoint(160, 70), port.AnchorOffset);
        Assert.True(raised, "MoveTo must raise PropertyChanged(AnchorOffset) so the canvas re-renders the port");
    }
}
