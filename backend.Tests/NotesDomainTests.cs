using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Tests.Infrastructure;

namespace Delegat.Tests;

public class NotesDomainTests
{
    [Fact]
    public async Task Note_AddFill_CreatesNoteForProduct()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var product = data.AddProduct(data.AddStore().Id);
        var note = new Note();

        Assert.True(note.AddFill(new Note.AddForm(new Note.UserAddForm { text = "Check packaging", importance = Importance.High }, product.Id)));
        db.notes.Add(note);
        await data.SaveAsync();

        var saved = db.notes.Single();
        Assert.Equal(product.Id, saved.productId);
        Assert.Equal("Check packaging", saved.text);
        Assert.Equal(Importance.High, saved.importance);
    }

    [Fact]
    public async Task Note_UpdateFill_UpdatesTextAndImportance()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var product = data.AddProduct(data.AddStore().Id);
        var note = new Note { productId = product.Id, text = "Draft", importance = Importance.Low };
        db.notes.Add(note);
        await data.SaveAsync();

        Assert.True(note.UpdateFill(new Note.UpdateForm { text = "Final", importance = Importance.High }));
        await db.SaveChangesAsync();

        Assert.Equal("Final", db.notes.Single().text);
        Assert.Equal(Importance.High, db.notes.Single().importance);
    }

    [Fact]
    public async Task Note_UpdateFill_IgnoresEmptyText()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var product = data.AddProduct(data.AddStore().Id);
        var note = new Note { productId = product.Id, text = "Keep", importance = Importance.Medium };
        db.notes.Add(note);
        await data.SaveAsync();

        note.UpdateFill(new Note.UpdateForm { text = "" });
        await db.SaveChangesAsync();

        Assert.Equal("Keep", db.notes.Single().text);
    }

    [Fact]
    public async Task Note_Delete_RemovesOnlySelectedNote()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var product = data.AddProduct(data.AddStore().Id);
        var first = new Note { productId = product.Id, text = "First", importance = Importance.Low };
        var second = new Note { productId = product.Id, text = "Second", importance = Importance.High };
        db.notes.AddRange(first, second);
        await data.SaveAsync();

        db.notes.Remove(first);
        await db.SaveChangesAsync();

        Assert.Single(db.notes);
        Assert.Equal("Second", db.notes.Single().text);
    }

    [Fact]
    public async Task Notes_AreIsolatedByProduct()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var firstProduct = data.AddProduct(store.Id);
        var secondProduct = data.AddProduct(store.Id);
        db.notes.AddRange(
            new Note { productId = firstProduct.Id, text = "First product", importance = Importance.Medium },
            new Note { productId = secondProduct.Id, text = "Second product", importance = Importance.Medium });
        await data.SaveAsync();

        var notes = db.notes.Where(n => n.productId == firstProduct.Id).ToList();
        Assert.Single(notes);
        Assert.Equal("First product", notes[0].text);
    }

    [Fact]
    public async Task Notes_ProductIsolationExtendsAcrossStores()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var firstProduct = data.AddProduct(data.AddStore("A").Id);
        var secondProduct = data.AddProduct(data.AddStore("B").Id);
        db.notes.AddRange(
            new Note { productId = firstProduct.Id, text = "A note", importance = Importance.Low },
            new Note { productId = secondProduct.Id, text = "B note", importance = Importance.High });
        await data.SaveAsync();

        Assert.Single(db.notes.Where(n => n.productId == firstProduct.Id));
        Assert.DoesNotContain(db.notes.Where(n => n.productId == firstProduct.Id), n => n.text == "B note");
    }

    [Theory]
    [InlineData(Importance.Low)]
    [InlineData(Importance.Medium)]
    [InlineData(Importance.High)]
    public async Task Note_Importance_Persists(Importance importance)
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var note = new Note { productId = data.AddProduct(data.AddStore().Id).Id, text = "Priority", importance = importance };
        db.notes.Add(note);
        await data.SaveAsync();

        Assert.Equal(importance, db.notes.Single().importance);
    }
}
