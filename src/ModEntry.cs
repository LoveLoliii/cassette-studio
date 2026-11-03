using System;
using System.Linq;

namespace MondayCassettePack;

public sealed class ModEntry : IMod
{
    public const string ModId = PackConfig.ModId;
    private const string GiftBoxId = ModId + ".gift_box";
    private const string CourierId = ModId + ".courier";
    // A new parcel version also gives existing prototype recipients the gift box.
    private const string DueKey = PackConfig.ReceiptKey + "_due_day";
    private const string ReceivedKey = PackConfig.ReceiptKey + "_received";
    private const string ScheduleVersionKey = "delivery_schedule_version";
    private ModLog log;

    public void Init(ModManifest manifest) => log = new ModLog(manifest);

    public void OnEnable()
    {
        ModHook.OnModItemDirectoryInit += RegisterItems;
        // Init fires BEFORE save data is restored in Rev6. Use Normal instead.
        ModHook.OnGameLoadedNormal += InitializeDelivery;
        ModHook.OnNewGameStarted += OnNewGame;
        ModHook.OnGenerateCustomerVeryLate += QueueCourier;
    }

    public void OnDisable()
    {
        ModHook.OnModItemDirectoryInit -= RegisterItems;
        ModHook.OnGameLoadedNormal -= InitializeDelivery;
        ModHook.OnNewGameStarted -= OnNewGame;
        ModHook.OnGenerateCustomerVeryLate -= QueueCourier;
    }

    private static string Text(string key) => ModHelper.GetLocalized(ModId, key);
    private static string TapeId(TapeDefinition tape) => ModId + ".cassette_" + tape.Key;
    private static string TrackId(TapeDefinition tape) => ModId + ":track_" + tape.Key;

    private void RegisterItems(ModItemDirectory directory)
    {
        foreach (TapeDefinition tape in PackConfig.Tapes)
        {
            directory.Add(TapeId(tape), () => CreateTape(tape));
        }
        directory.Add(GiftBoxId, CreateEmptyGiftBox);
    }

    private static GameItem CreateTape(TapeDefinition tape)
    {
            // Uses the vanilla cassette artwork and behaviour, with our own audio.
            GameItem item = ItemDirectory.CreateEmptyItem(TapeId(tape))
                // The atlas has no cassette2 sprite. Item IDs and sprite names
                // are different; use the verified vanilla cassette10 artwork.
                .SetSpriteAndShape("Items/items_amenities", tape.Sprite);
            item.name = Text("tape_" + tape.Key + "_name");
            item.shortDescription = Text("tape_" + tape.Key + "_description");
            item.unitValue = PackConfig.TapeValue;
            item.SetGameItemType("ART");
            item.SetGameItemType("LUXURY_ITEM");
            CassetteHelper.InitCassetteItem(item, TrackId(tape));
            AudioHelper.InitCassette(item);
            return item;
    }

    private static GameItem CreateEmptyGiftBox()
    {
        var (window, inventory) = DirectoryUtils.CreateInventoryWindow(PackConfig.BoxWidth, PackConfig.BoxHeight);
        GameItem box = ItemDirectory.CreateEmptyItem(GiftBoxId).SetContentWindow(window)
            .SetSpriteAndShapeFromMod(ModId, PackConfig.GiftSprite);
        box.name = Text("gift_box_name");
        box.shortDescription = Text("gift_box_description");
        box.unitValue = 30;
        box.SetGameItemType("HOUSEHOLD_GOOD");
        ContainerHelper.InitContainerItem(inventory, box);
        ContainerHelper.AllowOnlyTaggedItem(inventory, CassetteHelper.CASSETTE_TAG,
            ownedOnly: true, excludeContainer: true);
        ContainerHelper.AddContainerTooltipTag(box);
        // Keep the factory empty: the save loader restores the actual contents.
        // Refilling here would regenerate removed cassettes whenever a save loads.
        return box;
    }

    private static GameItem CreateFilledGiftBox()
    {
        GameItem box = ModHelper.SetItemOwnerShip(ModHelper.SpawnItem(GiftBoxId), true);
        GraphNodeStorage inventory = box.contentWindow.childElement as GraphNodeStorage;
        try
        {
            foreach (TapeDefinition definition in PackConfig.Tapes)
            {
                GameItem tape = ModHelper.SetItemOwnerShip(ModHelper.SpawnItem(TapeId(definition)), true);
                GraphUtils.TryAcceptAll(inventory, tape);
                if (tape.parentInventory == null)
                {
                    tape.Destroy();
                    throw new InvalidOperationException("Gift box cannot hold all cassettes.");
                }
            }
            return box;
        }
        catch
        {
            box.Destroy();
            throw;
        }
    }

    private void OnNewGame(string scenarioId) => InitializeDelivery();

    private void InitializeDelivery()
    {
        if (ModHelper.GetModDataBool(ModId, ReceivedKey)) return;
        // The gift parcel has separate receipt keys from the single-tape prototype.
        if (ModHelper.GetModDataInt(ModId, ScheduleVersionKey) >= 2 &&
            ModHelper.GetModDataInt(ModId, DueKey) > 0) return;
        int day = StoreStation.Instance.dayCounter + PackConfig.DeliveryDelayDays;
        ModHelper.SetModDataInt(ModId, DueKey, day);
        ModHelper.SetModDataInt(ModId, ScheduleVersionKey, 2);
        log.Log("Cassette delivery scheduled for game day " + day + ".");
    }

    private void QueueCourier(StoreClientManager manager)
    {
        InitializeDelivery();
        if (!DeliverySchedule.IsDue(StoreStation.Instance.dayCounter,
                ModHelper.GetModDataInt(ModId, DueKey),
                ModHelper.GetModDataBool(ModId, ReceivedKey))) return;
        if (manager.clientStack.Any(c => c != null && c.identifier == CourierId)) return;
        // Do not schedule again if reopening the shutter while this NPC is present.
        if (PlayerStore.Instance.currentClientInstance?.GetClientBlueprint()?.identifier == CourierId) return;
        manager.AddClient(CreateCourier());
        log.Log("Cassette courier added to today's visitor queue.");
    }

    private StoreClient CreateCourier()
    {
        var client = new StoreClient
        {
            identifier = CourierId,
            displayName = Text("courier_name"),
            clientFaction = StoreClient.FACTION_LOWER,
            spriteName = SpriteDict.GetRandomLowerLevelSpriteName(),
            clientIntent = StoreClient.ClientIntent.DIALOGUE,
            dismissable = false,
            arrestable = false,
            nonShootable = true
        };
        Dialogue handover = client.mainDialogue.SetText(client.displayName, Text("courier_intro"));
        Dialogue result = handover.NextDialogue().SetText(client.displayName, Text("courier_done"));
        handover.SetEndAction(() => Deliver(client, result));
        client.CompleteClientCreation();
        return client;
    }

    private void Deliver(StoreClient courier, Dialogue handover)
    {
        bool received = ModHelper.GetModDataBool(ModId, ReceivedKey);
        if (!received)
        {
            if (PackConfig.Tapes.Any(tape => !AudioManager.Instance.CanPlay(TrackId(tape))))
            {
                Finish(courier, handover, "audio_missing");
                return;
            }
            GameItem cassette = CreateFilledGiftBox();
            // TryAcceptAll returns the UNPLACED count (0 means success).
            GraphUtils.TryAcceptAll(EmporiumEntry.Instance.frontInvinvElement, cassette);
            if (cassette.parentInventory == null)
                GraphUtils.TryAcceptAll(EmporiumEntry.Instance.invElement, cassette);
            if (cassette.parentInventory == null)
                GraphUtils.TryAcceptAll(EmporiumEntry.Instance.backInvinvElement, cassette);
            if (cassette.parentInventory == null)
            {
                cassette.Destroy();
                Finish(courier, handover, "no_space");
                return;
            }
            ModHelper.SetModDataBool(ModId, ReceivedKey, true);
            log.Log("Cassette delivered. Receipt will persist with the next game save.");
        }
        Finish(courier, handover, "courier_done");
    }

    private static void Finish(StoreClient courier, Dialogue handover, string key)
    {
        handover.SetText(courier.displayName, Text(key)).SetEndAction(() =>
            PlayerStore.Instance.DismissCurrentClient(callNextWhenPossible: false));
    }
}
