#nowarn "0044"
module Eru.Tui.Browse.CollectionsPane

open Terminal.Gui.App
open Terminal.Gui.Drawing
open Terminal.Gui.ViewBase
open Terminal.Gui.Views

type CollectionsPane() as this =
    inherit View()

    let label = new Label()

    do
        this.CanFocus <- true
        BrowseTheme.apply BrowseTheme.Main this

        label.Text <- "Collections — coming soon"
        label.X <- Pos.Center()
        label.Y <- Pos.Center()
        BrowseTheme.apply BrowseTheme.Muted label
        this.Add(label :> View) |> ignore

    member _.FocusContent() = this.SetFocus() |> ignore
