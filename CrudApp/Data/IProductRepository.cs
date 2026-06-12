using System.Collections.Generic;
using CrudApp.Models;

namespace CrudApp.Data
{
    public interface IProductRepository
    {
        IEnumerable<Product> GetAll();
        Product? GetById(int id);
        Product Create(Product product);
        Product? Update(int id, Product product);
        bool Delete(int id);
    }
}
