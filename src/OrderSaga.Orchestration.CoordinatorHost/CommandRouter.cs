using OrderSaga.Orchestration;
using OrderSaga.Orchestration.Messaging;

namespace OrderSaga.Orchestration.CoordinatorHost;

/// <summary>
/// The one piece of genuinely Coordinator-specific routing logic: which of the two command queues
/// a given command type belongs to. Everything not an Inventory command is a stateless-responder
/// command -- accurate given the fixed, known set of five command types.
/// </summary>
public sealed class CommandRouter
{
    private static readonly HashSet<Type> InventoryCommandTypes =
    [
        typeof(ReserveStockCommand),
        typeof(ConfirmReservationCommand),
        typeof(ReleaseReservationCommand),
    ];

    private readonly IMessagePublisher _inventoryPublisher;
    private readonly IMessagePublisher _statelessResponderPublisher;

    public CommandRouter(IMessagePublisher inventoryPublisher, IMessagePublisher statelessResponderPublisher)
    {
        _inventoryPublisher = inventoryPublisher;
        _statelessResponderPublisher = statelessResponderPublisher;
    }

    public IMessagePublisher PublisherFor(Type commandType) =>
        InventoryCommandTypes.Contains(commandType) ? _inventoryPublisher : _statelessResponderPublisher;
}
